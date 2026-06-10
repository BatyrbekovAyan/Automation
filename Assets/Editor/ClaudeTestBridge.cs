// ClaudeTestBridge.cs
// Lets a terminal agent (Claude Code) run this project's EditMode tests inside the
// already-open Unity Editor and read structured results back — without clicking the GUI.
//
// Handshake (all files live under <project>/Temp/claude/, which Unity already gitignores):
//   IN   run-tests.trigger   Drop this file to request a run.
//                              - empty            -> run all EditMode tests
//                              - one regex/line   -> matched against each test's full name
//   OUT  test-summary.json    { status: running|completed|error, counts, failures[] }
//   OUT  test-results.xml     Full NUnit result XML (best-effort)
//
// The Editor polls for the trigger ~2x/second while it has focus. If Unity is unfocused
// it may defer the poll until you click its window — that is expected.
//
// Compile safety: scripts edited while the Editor was unfocused are not compiled yet when
// the trigger is noticed, and TestRunnerApi.Execute would run the STALE assemblies (and
// report green for code it never saw). So the bridge refreshes the AssetDatabase first and
// leaves the trigger file armed until compilation (and any domain reload) finishes — the
// trigger is only consumed, and the run only started, against freshly compiled assemblies.
// test-summary.json carries editorAssemblyWrittenUtc (Assembly-CSharp-Editor.dll mtime at
// run start) so a stale run is detectable after the fact; `total` is the executed count.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ClaudeTools
{
    [InitializeOnLoad]
    public static class ClaudeTestBridge
    {
        const double PollSeconds = 0.5;

        // Resolved from Application.dataPath so paths are correct regardless of the
        // Editor's actual working directory (Hub-launched Unity is NOT rooted at the project).
        static string _root;
        static string Root => _root ??= Directory.GetParent(Application.dataPath).FullName;
        static string Dir => Path.Combine(Root, "Temp", "claude");
        static string TriggerFile => Path.Combine(Dir, "run-tests.trigger");
        static string SummaryFile => Path.Combine(Dir, "test-summary.json");
        static string ResultsXmlFile => Path.Combine(Dir, "test-results.xml");

        static double _nextPoll;
        static bool _running;
        // True once we've issued the pre-run AssetDatabase.Refresh for the armed trigger.
        // Deliberately NOT persisted: a compile's domain reload resets it, so after the
        // reload we refresh once more (a cheap no-op) before consuming the trigger.
        static bool _refreshIssued;
        static string _assemblyStampForRun;
        static TestRunnerApi _api;
        static Callbacks _callbacks;

        static ClaudeTestBridge()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollSeconds;

            try
            {
                if (!File.Exists(TriggerFile))
                {
                    _refreshIssued = false;
                    return;
                }

                if (_running)
                {
                    File.Delete(TriggerFile); // consume so it can't fire after this run ends
                    Debug.LogWarning("[ClaudeTestBridge] Trigger ignored — a run is already in progress.");
                    return;
                }

                // Leave the trigger armed while Unity compiles or imports: the file survives
                // the domain reload, and consuming it now would execute stale assemblies.
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

                if (!_refreshIssued)
                {
                    _refreshIssued = true;
                    AssetDatabase.Refresh();
                    return; // give a queued compile one tick to flip isCompiling / reload the domain
                }

                // Compilation is done. If it failed, Unity kept the OLD assemblies — running
                // now would silently test stale code, so report an error result instead.
                if (EditorUtility.scriptCompilationFailed)
                {
                    _refreshIssued = false;
                    File.Delete(TriggerFile);
                    WriteSummary(new Summary
                    {
                        status = "error",
                        overall = "CompilationFailed",
                        editorAssemblyWrittenUtc = EditorAssemblyTimestampUtc()
                    });
                    Debug.LogError("[ClaudeTestBridge] Scripts failed to compile — run aborted (stale assemblies would have been tested). Fix errors and re-trigger.");
                    return;
                }

                _refreshIssued = false;
                string content = SafeRead(TriggerFile);
                File.Delete(TriggerFile); // consume only now, against fresh assemblies

                StartRun(content);
            }
            catch (Exception e)
            {
                Debug.LogError("[ClaudeTestBridge] Tick failed: " + e);
            }
        }

        static void StartRun(string triggerContent)
        {
            Directory.CreateDirectory(Dir);
            _running = true;
            _assemblyStampForRun = EditorAssemblyTimestampUtc();

            var filter = new Filter { testMode = TestMode.EditMode };
            var groups = ParseGroups(triggerContent);
            if (groups.Length > 0) filter.groupNames = groups;

            WriteSummary(new Summary
            {
                status = "running",
                overall = "Running",
                startedAt = EditorApplication.timeSinceStartup,
                editorAssemblyWrittenUtc = _assemblyStampForRun
            });

            Debug.Log("[ClaudeTestBridge] Starting EditMode test run " +
                      (groups.Length > 0 ? "(filter: " + string.Join(", ", groups) + ")" : "(all tests)"));

            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _callbacks = new Callbacks();
            _api.RegisterCallbacks(_callbacks);
            _api.Execute(new ExecutionSettings(filter));
        }

        static string[] ParseGroups(string content)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(content))
            {
                foreach (var raw in content.Split('\n'))
                {
                    var s = raw.Trim();
                    if (s.Length > 0) list.Add(s);
                }
            }
            return list.ToArray();
        }

        static void Finish(ITestResultAdaptor result)
        {
            try
            {
                var failures = new List<Failure>();
                CollectFailures(result, failures);

                var summary = new Summary
                {
                    status = "completed",
                    overall = result.TestStatus.ToString(),
                    passed = result.PassCount,
                    failed = result.FailCount,
                    skipped = result.SkipCount,
                    inconclusive = result.InconclusiveCount,
                    total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount,
                    durationSeconds = result.Duration,
                    finishedAt = EditorApplication.timeSinceStartup,
                    editorAssemblyWrittenUtc = string.IsNullOrEmpty(_assemblyStampForRun)
                        ? EditorAssemblyTimestampUtc()
                        : _assemblyStampForRun,
                    failures = failures.ToArray()
                };

                WriteSummary(summary);
                TryWriteXml(result);

                if (summary.failed > 0)
                    Debug.LogError($"[ClaudeTestBridge] DONE — FAILED  passed={summary.passed} failed={summary.failed} skipped={summary.skipped} total={summary.total}");
                else
                    Debug.Log($"[ClaudeTestBridge] DONE — PASSED  passed={summary.passed} skipped={summary.skipped} total={summary.total}");
            }
            catch (Exception e)
            {
                Debug.LogError("[ClaudeTestBridge] Finish failed: " + e);
            }
            finally
            {
                if (_api != null && _callbacks != null) _api.UnregisterCallbacks(_callbacks);
                if (_api != null) UnityEngine.Object.DestroyImmediate(_api);
                _api = null;
                _callbacks = null;
                _running = false;
            }
        }

        static void CollectFailures(ITestResultAdaptor r, List<Failure> acc)
        {
            if (r.HasChildren)
            {
                foreach (var c in r.Children) CollectFailures(c, acc);
                return;
            }

            if (r.TestStatus == TestStatus.Failed)
            {
                acc.Add(new Failure
                {
                    name = r.Test != null ? r.Test.FullName : r.Name,
                    message = r.Message,
                    stackTrace = r.StackTrace
                });
            }
        }

        static void TryWriteXml(ITestResultAdaptor result)
        {
            try { File.WriteAllText(ResultsXmlFile, result.ToXml().OuterXml); }
            catch (Exception e) { Debug.LogWarning("[ClaudeTestBridge] Could not write results XML: " + e.Message); }
        }

        static void WriteSummary(Summary s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(SummaryFile, JsonUtility.ToJson(s, true));
            }
            catch (Exception e) { Debug.LogError("[ClaudeTestBridge] Could not write summary: " + e.Message); }
        }

        static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); } catch { return ""; }
        }

        // The EditMode tests compile into Assembly-CSharp-Editor (no asmdef), so its dll
        // mtime identifies exactly which build of the test code a run executed.
        static string EditorAssemblyTimestampUtc()
        {
            try
            {
                var dll = Path.Combine(Root, "Library", "ScriptAssemblies", "Assembly-CSharp-Editor.dll");
                return File.Exists(dll) ? File.GetLastWriteTimeUtc(dll).ToString("o") : "";
            }
            catch { return ""; }
        }

        class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void RunFinished(ITestResultAdaptor result) { Finish(result); }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }

        [Serializable]
        class Summary
        {
            public int schema = 1;
            public string status;
            public string overall;
            public int total;
            public int passed;
            public int failed;
            public int skipped;
            public int inconclusive;
            public double durationSeconds;
            public double startedAt;
            public double finishedAt;
            public string editorAssemblyWrittenUtc;
            public Failure[] failures = new Failure[0];
        }

        [Serializable]
        class Failure
        {
            public string name;
            public string message;
            public string stackTrace;
        }
    }
}
