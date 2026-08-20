namespace IW4.Studio.Desktop;
using System.Reflection;

public static class AssemblyConst
{
    public const string RepositoryUrl = "https://github.com/jacob-schroeder/IW4Studio";
    public const string Author = "Jacob Schroeder";
    public const string Platform = "Playstation 3";
    public const string PlatformAbbr = "PS3";
    
    public static string AssemblyVersion
    {
        get
        {
            // Get version of the main application (entry point)
            Version? version = Assembly.GetEntryAssembly()?.GetName().Version;

            string versionString = "Unknown";

            if (version != null)
                versionString = $"{version.Major}.{version.Minor}";

            return versionString;
        }
    }
}