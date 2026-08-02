using System.ComponentModel.DataAnnotations;

namespace IW4.FastFiles.Database;

public enum XFileLanguage : int
{
    [Display(Name = "English (US)")]
    LANGUAGE_ENGLISH = 0x1,
    [Display(Name = "French")]
    LANGUAGE_FRENCH = 0x2,
    [Display(Name = "German")]
    LANGUAGE_GERMAN = 0x3,
    [Display(Name = "Italian")]
    LANGUAGE_ITALIAN = 0x4,
    [Display(Name = "Spanish")]
    LANGUAGE_SPANISH = 0x5,
    [Display(Name = "English (UK)")]
    LANGUAGE_BRITISH = 0x6,
    [Display(Name = "Russian")]
    LANGUAGE_RUSSIAN = 0x7,
    [Display(Name = "Polish")]
    LANGUAGE_POLISH = 0x8,
    [Display(Name = "Korean")]
    LANGUAGE_KOREAN = 0x9,
    [Display(Name = "Taiwanese")]
    LANGUAGE_TAIWANESE = 0xA,
    [Display(Name = "Japanese")]
    LANGUAGE_JAPANESE = 0xB,
    [Display(Name = "Chinese")]
    LANGUAGE_CHINESE = 0xC,
    [Display(Name = "Thai")]
    LANGUAGE_THAI = 0xD,
    [Display(Name = "Leet")]
    LANGUAGE_LEET = 0xE,
    [Display(Name = "Czech")]
    LANGUAGE_CZECH = 0xF,
    COUNT
};