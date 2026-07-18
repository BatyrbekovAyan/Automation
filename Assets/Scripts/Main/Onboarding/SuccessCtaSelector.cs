public enum SuccessCta { UploadPriceList, OpenChats }

// RED stub — real logic lands in the GREEN commit. Throws so the
// SuccessCtaSelector tests run-and-fail rather than vacuously pass.
public static class SuccessCtaSelector
{
    public static SuccessCta Choose(bool hasUploadedFiles) => throw new System.NotImplementedException();
}
