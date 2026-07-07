using NUnit.Framework;
using UnityEngine;

public class DashboardStatusInfoTests
{
    [Test] public void LabelsAreRussian()
    {
        Assert.AreEqual("Заявка", DashboardStatusInfo.Label(OutcomeStatus.OrderCollected));
        Assert.AreEqual("Нужен владелец", DashboardStatusInfo.Label(OutcomeStatus.OwnerNeeded));
        Assert.AreEqual("Клиент замолчал", DashboardStatusInfo.Label(OutcomeStatus.ClientSilent));
    }

    [Test] public void OrderCollectedUsesPillGreen()
    {
        ColorUtility.TryParseHtmlString("#34C759", out var fg);
        Assert.AreEqual(fg, DashboardStatusInfo.FgColor(OutcomeStatus.OrderCollected));
    }

    [Test] public void OrderedHasFiveStatusesOrderCollectedFirst()
    {
        Assert.AreEqual(5, DashboardStatusInfo.Ordered.Length);
        Assert.AreEqual(OutcomeStatus.OrderCollected, DashboardStatusInfo.Ordered[0]);
    }
}
