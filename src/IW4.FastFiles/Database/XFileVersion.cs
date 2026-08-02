using System.ComponentModel.DataAnnotations;

namespace IW4.FastFiles.Database;

public enum XFileVersion : int
{
    [Display(Name = "Modern Warfare 2")]
    ModernWarfare2 = 0x10D
}