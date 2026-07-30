using NUnit.Framework;

// Contract tests for the in-flight price-list upload registry.
//
// This exists because uploads used to run as coroutines on the BotSettings
// MonoBehaviour: leaving the screen deactivates that GameObject, Unity kills
// its coroutines permanently, and the upload neither completed nor failed —
// the row span pulsing «Загрузка…» forever while n8n happily finished
// ingesting the file under a fileId the app had already forgotten.
//
// The registry is deliberately a plain C# class with no view lifecycle of its
// own: a job lives until it is explicitly completed, failed, or dismissed.
public class UploadJobRegistryTests
{
    private const string BotA = "TESTBOT_ujr_A";
    private const string BotB = "TESTBOT_ujr_B";
    private const string Product = "product";
    private const string Service = "service";

    private UploadJobRegistry registry;

    [SetUp]
    public void SetUp() => registry = new UploadJobRegistry();

    private UploadJob AddJob(string bot = BotA, string type = Product, string name = "price.pdf") =>
        registry.Add(bot, type, $"/tmp/{name}", name);

    [Test]
    public void Add_ListsJobUnderItsBotAndContentType()
    {
        UploadJob job = AddJob();

        var jobs = registry.JobsFor(BotA, Product);
        Assert.AreEqual(1, jobs.Count);
        Assert.AreSame(job, jobs[0]);
        Assert.AreEqual("price.pdf", jobs[0].FileName);
    }

    [Test]
    public void Add_StartsInUploadingState()
    {
        Assert.AreEqual(UploadJobState.Uploading, AddJob().State);
    }

    [Test]
    public void Add_MintsUniqueNonEmptyFileId()
    {
        string first = AddJob(name: "a.pdf").FileId;
        string second = AddJob(name: "b.pdf").FileId;

        Assert.IsNotEmpty(first);
        Assert.IsNotEmpty(second);
        Assert.AreNotEqual(first, second);
    }

    [Test]
    public void JobsFor_PreservesInsertionOrder()
    {
        AddJob(name: "a.pdf");
        AddJob(name: "b.pdf");
        AddJob(name: "c.pdf");

        var names = registry.JobsFor(BotA, Product).ConvertAll(j => j.FileName);
        CollectionAssert.AreEqual(new[] { "a.pdf", "b.pdf", "c.pdf" }, names);
    }

    [Test]
    public void JobsFor_IsolatesByBot()
    {
        AddJob(bot: BotA);

        Assert.AreEqual(1, registry.JobsFor(BotA, Product).Count);
        Assert.AreEqual(0, registry.JobsFor(BotB, Product).Count);
    }

    [Test]
    public void JobsFor_IsolatesByContentType()
    {
        AddJob(type: Product);

        Assert.AreEqual(1, registry.JobsFor(BotA, Product).Count);
        Assert.AreEqual(0, registry.JobsFor(BotA, Service).Count);
    }

    [Test]
    public void JobsFor_UnknownBot_ReturnsEmpty()
    {
        Assert.AreEqual(0, registry.JobsFor("nobody", Product).Count);
    }

    // The regression that started all this: nothing about a job is tied to a
    // view being open. Closing Bot Settings must not strand or drop the job.
    [Test]
    public void Job_StaysUploading_UntilExplicitlyResolved()
    {
        UploadJob job = AddJob();

        var jobs = registry.JobsFor(BotA, Product);
        Assert.AreEqual(1, jobs.Count);
        Assert.AreEqual(UploadJobState.Uploading, job.State);
    }

    [Test]
    public void MarkFailed_SetsStateReasonAndRetryFlag()
    {
        UploadJob job = AddJob();

        registry.MarkFailed(job, UploadFailureText.EmptyFile, canRetry: false);

        Assert.AreEqual(UploadJobState.Failed, job.State);
        Assert.AreEqual(UploadFailureText.EmptyFile, job.FailureReason);
        Assert.IsFalse(job.CanRetry);
    }

    // A failed job must stay listed — the reopened screen is where the user
    // finds out it failed and taps to retry or dismisses it.
    [Test]
    public void MarkFailed_KeepsJobListed()
    {
        UploadJob job = AddJob();

        registry.MarkFailed(job, UploadFailureText.TapToRetry, canRetry: true);

        Assert.AreEqual(1, registry.JobsFor(BotA, Product).Count);
    }

    [Test]
    public void MarkUploading_ClearsPreviousFailure()
    {
        UploadJob job = AddJob();
        registry.MarkFailed(job, UploadFailureText.TapToRetry, canRetry: true);

        registry.MarkUploading(job);

        Assert.AreEqual(UploadJobState.Uploading, job.State);
        Assert.IsNull(job.FailureReason);
    }

    // Retrying reuses the job but must mint a fresh fileId: the abandoned
    // attempt may already have inserted chunks server-side under the old one.
    [Test]
    public void MarkUploading_MintsFreshFileId()
    {
        UploadJob job = AddJob();
        string original = job.FileId;
        registry.MarkFailed(job, UploadFailureText.TapToRetry, canRetry: true);

        registry.MarkUploading(job);

        Assert.AreNotEqual(original, job.FileId);
        Assert.IsNotEmpty(job.FileId);
    }

    [Test]
    public void Remove_DropsJob()
    {
        UploadJob job = AddJob();

        Assert.IsTrue(registry.Remove(job));
        Assert.AreEqual(0, registry.JobsFor(BotA, Product).Count);
    }

    [Test]
    public void Remove_UnknownJob_ReturnsFalse()
    {
        UploadJob job = AddJob();
        registry.Remove(job);

        Assert.IsFalse(registry.Remove(job));
    }

    [Test]
    public void Remove_Null_ReturnsFalse()
    {
        Assert.IsFalse(registry.Remove(null));
    }

    [Test]
    public void RemoveForBot_DropsThatBotsJobsInBothTabsOnly()
    {
        AddJob(bot: BotA, type: Product);
        AddJob(bot: BotA, type: Service);
        AddJob(bot: BotB, type: Product);

        registry.RemoveForBot(BotA);

        Assert.AreEqual(0, registry.JobsFor(BotA, Product).Count);
        Assert.AreEqual(0, registry.JobsFor(BotA, Service).Count);
        Assert.AreEqual(1, registry.JobsFor(BotB, Product).Count);
    }

    [Test]
    public void OnChanged_FiresOnAdd()
    {
        int fired = 0;
        registry.OnChanged += () => fired++;

        AddJob();

        Assert.AreEqual(1, fired);
    }

    [Test]
    public void OnChanged_FiresOnMarkFailed()
    {
        UploadJob job = AddJob();
        int fired = 0;
        registry.OnChanged += () => fired++;

        registry.MarkFailed(job, UploadFailureText.TapToRetry, canRetry: true);

        Assert.AreEqual(1, fired);
    }

    [Test]
    public void OnChanged_FiresOnRemove()
    {
        UploadJob job = AddJob();
        int fired = 0;
        registry.OnChanged += () => fired++;

        registry.Remove(job);

        Assert.AreEqual(1, fired);
    }

    [Test]
    public void OnChanged_DoesNotFire_WhenRemoveFindsNothing()
    {
        UploadJob job = AddJob();
        registry.Remove(job);
        int fired = 0;
        registry.OnChanged += () => fired++;

        registry.Remove(job);

        Assert.AreEqual(0, fired);
    }

    [Test]
    public void Add_RejectsBlankBotName()
    {
        Assert.IsNull(registry.Add("", Product, "/tmp/a.pdf", "a.pdf"));
        Assert.AreEqual(0, registry.JobsFor("", Product).Count);
    }

    [Test]
    public void Add_CarriesFilePathAndDisplayNameOverride()
    {
        UploadJob job = registry.Add(BotA, Product, "/tmp/pickedMedia0.jpg", "Прайс 1.jpg", "Прайс 1.jpg");

        Assert.AreEqual("/tmp/pickedMedia0.jpg", job.FilePath);
        Assert.AreEqual("Прайс 1.jpg", job.FileName);
        Assert.AreEqual("Прайс 1.jpg", job.DisplayNameOverride);
    }

    /////////////////////////////// CANCELLATION ///////////////////////////////
    // Removing a job from the list does NOT stop the coroutine uploading it —
    // that coroutine lives on UploadCenter and holds its own reference. Without
    // a sticky flag on the job itself, deleting a bot mid-upload let the success
    // path re-create the PlayerPrefs keys the delete had just removed.

    [Test]
    public void Add_StartsNotCancelled()
    {
        Assert.IsFalse(AddJob().Cancelled);
    }

    [Test]
    public void RemoveForBot_MarksTheRemovedJobsCancelled()
    {
        UploadJob mine = AddJob(bot: BotA);
        UploadJob other = AddJob(bot: BotB);

        registry.RemoveForBot(BotA);

        Assert.IsTrue(mine.Cancelled, "a deleted bot's in-flight upload must be flagged, not just delisted");
        Assert.IsFalse(other.Cancelled);
    }

    [Test]
    public void Remove_MarksTheJobCancelled()
    {
        UploadJob job = AddJob();

        registry.Remove(job);

        Assert.IsTrue(job.Cancelled);
    }

    // Cancellation is terminal: a retry must never revive a job whose bot is gone.
    [Test]
    public void MarkUploading_DoesNotUncancel()
    {
        UploadJob job = AddJob();
        registry.RemoveForBot(BotA);

        registry.MarkUploading(job);

        Assert.IsTrue(job.Cancelled);
    }

    ///////////////////////////// REPLACE CONSENT /////////////////////////////
    // The stale-chunk delete after a successful upload may only run when the
    // user actually answered «Заменить?». Retry re-enters the upload BELOW that
    // question, so without carrying the answer a retry deleted silently.

    [Test]
    public void Add_DefaultsToNoReplaceConsent()
    {
        Assert.IsFalse(AddJob().ReplaceConfirmed);
    }

    [Test]
    public void Add_CarriesReplaceConsent()
    {
        UploadJob job = registry.Add(BotA, Product, "/tmp/p.pdf", "p.pdf", null, replaceConfirmed: true);

        Assert.IsTrue(job.ReplaceConfirmed);
    }

    [Test]
    public void MarkUploading_PreservesReplaceConsentAcrossRetry()
    {
        UploadJob job = registry.Add(BotA, Product, "/tmp/p.pdf", "p.pdf", null, replaceConfirmed: true);
        registry.MarkFailed(job, UploadFailureText.TapToRetry, canRetry: true);

        registry.MarkUploading(job);

        Assert.IsTrue(job.ReplaceConfirmed, "consent given at pick time still stands for the retry");
    }
}
