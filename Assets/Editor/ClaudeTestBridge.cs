// ClaudeTestBridge.cs
// Lets a terminal agent (Claude Code) run this project's EditMode tests inside the
// already-open Unity Editor and read structured results back — without clicking the GUI.
//
// Handshake (all files live under <project>/Temp/claude/, which Unity already gitignores):
//   IN   run-tests.trigger   Drop this file to request a run.
//                              - empty            -> run all EditMode tests
//                              - one regex/line   -> matched against each test's full name
//   OUT  test-summary.json    { status: running|completed, counts, failures[] }
//   OUT  test-results.xml     Full NUnit result XML (best-effort)
//
// The Editor polls for the trigger ~2x/second while it has focus. If Unity is unfocused
// it may defer the poll until you click its window — that is expected.

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
                if (!File.Exists(TriggerFile)) return;

                string content = SafeRead(TriggerFile);
                File.Delete(TriggerFile); // consume immediately so we never double-fire

                if (_running)
                {
                    Debug.LogWarning("[ClaudeTestBridge] Trigger ignored — a run is already in progress.");
                    return;
                }

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

            var filter = new Filter { testMode = TestMode.EditMode };
            var groups = ParseGroups(triggerContent);
            if (groups.Length > 0) filter.groupNames = groups;

            WriteSummary(new Summary { status = "running", overall = "Running", startedAt = EditorApplication.timeSinceStartup });

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
