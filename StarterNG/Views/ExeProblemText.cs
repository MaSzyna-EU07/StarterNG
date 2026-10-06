using StarterNG.Domain.Settings;

namespace StarterNG.Views;

public static class ExeProblemText
{
    /// <summary>The language key explaining why a simulator binary cannot be used as it is.</summary>
    public static string Key(ExeProblem problem) => problem switch
    {
        ExeProblem.NotExecutable => "ExeNotExecutable",
        ExeProblem.WrongPlatform => "ExeWrongPlatform",
        _ => "ExeNotFound"
    };
}
