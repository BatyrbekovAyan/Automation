// RED stub — real logic lands in the GREEN commit. Throws so the OnboardingGate
// tests run-and-fail rather than vacuously pass.
public static class OnboardingGate
{
    public static bool ShouldShowCarousel(bool hasBots, bool seen) => throw new System.NotImplementedException();
    public static bool ShouldAutoFlagSeen(bool hasBots, bool seen) => throw new System.NotImplementedException();
}
