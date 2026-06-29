using NUnit.Framework;

public class WorkflowIdParseTests
{
    [Test]
    public void FullWorkflowObject_ExtractsId()
    {
        Assert.AreEqual("abc123XYZ",
            Manager.ExtractWorkflowId("{\"id\":\"abc123XYZ\",\"name\":\"Bot\",\"nodes\":[]}"));
    }

    [Test]
    public void TrimmedObject_ExtractsId()
    {
        Assert.AreEqual("xyz", Manager.ExtractWorkflowId("{\"id\":\"xyz\"}"));
    }

    [Test]
    public void WhitespaceAroundColon_ExtractsId()
    {
        Assert.AreEqual("spaced", Manager.ExtractWorkflowId("{ \"id\": \"spaced\" }"));
    }

    [Test]
    public void NoIdField_ReturnsNull()
    {
        Assert.IsNull(Manager.ExtractWorkflowId("{\"name\":\"Bot\"}"));
    }

    [Test]
    public void MalformedBody_ReturnsNull()
    {
        Assert.IsNull(Manager.ExtractWorkflowId("Workflow was started"));
    }

    [Test]
    public void EmptyString_ReturnsNull()
    {
        Assert.IsNull(Manager.ExtractWorkflowId(""));
    }

    [Test]
    public void Null_ReturnsNull()
    {
        Assert.IsNull(Manager.ExtractWorkflowId(null));
    }
}
