

namespace IW4.Studio.Desktop.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string RepositoryUrl => AssemblyConst.RepositoryUrl;
    public string Author => AssemblyConst.Author;
    public string Version => AssemblyConst.AssemblyVersion;
}