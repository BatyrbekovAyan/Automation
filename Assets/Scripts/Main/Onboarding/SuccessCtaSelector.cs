/// <summary>Primary-CTA target for the «Бот подключён!» success moment.</summary>
public enum SuccessCta { UploadPriceList, OpenChats }

/// <summary>
/// Pure selector for the success-panel primary CTA (analog: ChatRowSwipePolicy).
/// The MonoBehaviour supplies the "has files" fact from UploadedFilesStore
/// (both "product" and "service" content types) and routes the CTA accordingly.
/// </summary>
public static class SuccessCtaSelector
{
    /// <summary>
    /// Files already exist (settings re-auth case) ⇒ «Открыть чаты»; otherwise
    /// steer the owner to «Загрузить прайс-лист» so the fresh bot has a catalog.
    /// </summary>
    public static SuccessCta Choose(bool hasUploadedFiles) =>
        hasUploadedFiles ? SuccessCta.OpenChats : SuccessCta.UploadPriceList;
}
