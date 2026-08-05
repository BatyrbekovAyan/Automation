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

    // FgColor is theme-routed (it must agree with the ThemedColor bindings on the
    // same elements), so the contract is "resolves the status role under the
    // active theme", not any literal hex.
    [Test] public void FgColorFollowsTheThemeStatusRoles()
    {
        Assert.AreEqual(Theme.Color(ThemeRole.StatusOrderCollected),
                        DashboardStatusInfo.FgColor(OutcomeStatus.OrderCollected));
        Assert.AreEqual(Theme.Color(ThemeRole.StatusInDialog),
                        DashboardStatusInfo.FgColor(OutcomeStatus.InDialog));
        Assert.AreEqual(Theme.Color(ThemeRole.StatusQuestionClosed),
                        DashboardStatusInfo.FgColor(OutcomeStatus.QuestionClosed));
    }

    [Test] public void OrderedHasFiveStatusesOrderCollectedFirst()
    {
        Assert.AreEqual(5, DashboardStatusInfo.Ordered.Length);
        Assert.AreEqual(OutcomeStatus.OrderCollected, DashboardStatusInfo.Ordered[0]);
    }
}
