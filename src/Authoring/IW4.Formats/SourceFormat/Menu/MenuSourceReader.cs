using IW4.Game.Assets;
using IW4.Game.Assets.Menu;
using IW4.Game.Math;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Formats.SourceFormat.Menu;

internal sealed partial class MenuSourceReader
{
    private const int MaximumNesting = 64;
    private readonly HashSet<string> _activeFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Path, FunctionScope Parent), IReadOnlyList<MenuDefAsset>> _includedFiles = [];
    private string? _sourceRoot;
    private string? _sourcePath;
    private IReadOnlyList<Token> _tokens = [];
    private int _position;

    public BaseAsset Read(TextReader source, XAssetType type, string assetName, string? sourcePath)
    {
        _sourceRoot = sourcePath is null ? null : FindSourceRoot(sourcePath);
        if (sourcePath is not null)
            _activeFiles.Add(sourcePath);
        IReadOnlyList<MenuDefAsset> menus = ReadDocument(source, sourcePath);
        BindExpressions(menus);

        if (type == XAssetType.Menu)
        {
            if (menus.Count != 1)
                throw AtSource(sourcePath, 1,
                    $"A Menu asset requires one menuDef. This source contains {menus.Count}. Choose MenuFile to import a list.");
            menus[0].Window.Name = assetName;
            return menus[0];
        }

        MenuDefReference[] registrations = menus.Select((menu, index) => new MenuDefReference(
            index, new XPointer<MenuDefAsset>(-1, XPointerResolutionMode.AliasCell), menu)
        {
            SourceMenu = menu
        }).ToArray();
        return new MenuFileAsset
        {
            Name = assetName,
            NamePointer = new XPointer<string>(-1),
            MenuCount = registrations.Length,
            MenusPointer = registrations.Length == 0 ? default : new(-1),
            Menus = registrations
        };
    }

    private IReadOnlyList<MenuDefAsset> ReadDocument(TextReader reader, string? path)
    {
        IReadOnlyList<Token> previousTokens = _tokens;
        int previousPosition = _position;
        string? previousPath = _sourcePath;
        FunctionScope previousScope = _scope;
        _sourcePath = path;
        _tokens = Tokenize(reader.ReadToEnd(), path);
        _position = 0;
        _scope = new FunctionScope(previousScope);
        try
        {
            bool wrapped = Take("{");
            var menus = new List<MenuDefAsset>();
            while (!(wrapped ? Take("}") : Current.Kind == TokenKind.End))
            {
                if (Take(";"))
                    continue;
                string directive = Word();
                switch (directive.ToLowerInvariant())
                {
                    case "menudef":
                        menus.Add(ReadMenu());
                        break;
                    case "functiondef":
                        ReadFunction();
                        break;
                    case "loadmenu":
                        Expect("{");
                        string include = Text();
                        Expect("}");
                        menus.AddRange(ReadInclude(include));
                        break;
                    default:
                        throw Error($"Unsupported menu directive '{directive}'. Expected menuDef, loadMenu, or functionDef.");
                }
            }
            if (Current.Kind != TokenKind.End)
                throw Error("Unexpected text after the menu source block.");
            return menus;
        }
        finally
        {
            _sourcePath = previousPath;
            _tokens = previousTokens;
            _position = previousPosition;
            _scope = previousScope;
        }
    }

    private IReadOnlyList<MenuDefAsset> ReadInclude(string name)
    {
        if (_sourcePath is null || _sourceRoot is null)
            throw Error($"loadMenu '{name}' requires the source file and its companion files on disk.");

        string relative = SourceOutput.NormalizeOwnedAssetName(name, "Menu include");
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
            throw Error($"Menu include '{name}' must be a relative path.");
        string containingDirectory = Path.GetDirectoryName(_sourcePath)
            ?? throw Error("The menu source path has no containing directory.");
        string localPath = Path.GetFullPath(relative, containingDirectory);
        string rootPath = Path.GetFullPath(relative, _sourceRoot);
        bool relativeToRoot = relative.StartsWith("ui/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("ui_mp/", StringComparison.OrdinalIgnoreCase);
        string preferredPath = relativeToRoot ? rootPath : localPath;
        string alternatePath = relativeToRoot ? localPath : rootPath;
        string path = File.Exists(preferredPath) ? preferredPath : alternatePath;
        if (!File.Exists(path))
            throw Error($"Missing loadMenu source '{name}'. Keep the exported menu files together.");
        var cacheKey = (path, _scope);
        if (_includedFiles.TryGetValue(cacheKey, out IReadOnlyList<MenuDefAsset>? cached))
            return cached;
        if (_activeFiles.Count >= MaximumNesting || !_activeFiles.Add(path))
            throw Error($"Cyclic or excessively nested loadMenu source '{name}'.");
        try
        {
            using var reader = new StreamReader(path, MenuExchange.SourceEncoding);
            IReadOnlyList<MenuDefAsset> menus = ReadDocument(reader, path);
            _includedFiles.Add(cacheKey, menus);
            return menus;
        }
        finally
        {
            _activeFiles.Remove(path);
        }
    }

    private static string FindSourceRoot(string path)
    {
        var directory = new FileInfo(path).Directory
            ?? throw new InvalidDataException("The menu source path has no containing directory.");
        for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
        {
            if (current.Name.Equals("ui", StringComparison.OrdinalIgnoreCase) ||
                current.Name.Equals("ui_mp", StringComparison.OrdinalIgnoreCase))
                return current.Parent?.FullName ?? directory.FullName;
        }
        return directory.FullName;
    }

    private MenuDefAsset ReadMenu()
    {
        Expect("{");
        var window = new SourceWindow();
        var items = new List<ItemDefReference>();
        var keys = new List<(int Key, MenuEventHandlerSet Handlers)>();
        string? font = null, allowedBinding = null, soundLoop = null;
        int fullscreen = 0, fadeCycle = 0;
        float fadeClamp = 0, fadeAmount = 0, fadeIn = 0, blur = 0;
        Vec4 focusColor = new();
        Statement? visible = null, x = null, y = null, width = null, height = null;
        MenuEventHandlerSet? open = null, close = null, requestClose = null, escape = null;

        while (!Take("}"))
        {
            if (Take(";"))
                continue;
            string property = Word().ToLowerInvariant();
            if (ReadWindowProperty(property, window))
                continue;
            switch (property)
            {
                case "itemdef":
                    items.Add(new ItemDefReference(items.Count, new XPointer<ItemDefAsset>(-1), ReadItem()));
                    break;
                case "font": font = Text(); break;
                case "fullscreen": fullscreen = Integer(); break;
                case "allowedbinding": allowedBinding = Text(); break;
                case "soundloop": soundLoop = Text(); break;
                case "focuscolor": focusColor = ReadColor(); break;
                case "fadecycle": fadeCycle = Integer(); break;
                case "fadeclamp": fadeClamp = Number(); break;
                case "fadeamount": fadeAmount = Number(); break;
                case "fadeinamount": fadeIn = Number(); break;
                case "blurworld": blur = Number(); break;
                case "visible":
                    if (AtNumber)
                    {
                        if (Integer() != 0)
                            window.DynamicFlags |= WindowDynamicFlags.WINDOW_DYNAMIC_VISIBLE;
                    }
                    else
                        visible = ReadStatementProperty(when: true);
                    break;
                case "exp":
                    Expect("rect");
                    switch (Word().ToLowerInvariant())
                    {
                        case "x": x = ReadStatementProperty(); break;
                        case "y": y = ReadStatementProperty(); break;
                        case "w": width = ReadStatementProperty(); break;
                        case "h": height = ReadStatementProperty(); break;
                        default: throw Error("Expected X, Y, W, or H after exp rect.");
                    }
                    break;
                case "onopen": open = ReadHandlers(); break;
                case "onclose": close = ReadHandlers(); break;
                case "onrequestclose":
                case "oncloserequest": requestClose = ReadHandlers(); break;
                case "onesc": escape = ReadHandlers(); break;
                case "execkey":
                case "execkeyint":
                    ReadKey(property, keys);
                    break;
                default: throw Error($"Unsupported menuDef property '{property}'.");
            }
        }
        if (string.IsNullOrWhiteSpace(window.Name))
            throw Error("A menuDef requires a name.");

        ItemKeyHandler? handlers = BuildKeys(keys);
        return new MenuDefAsset
        {
            Window = window.Build(isItem: false),
            Font = font,
            FontPointer = Inline(font),
            Fullscreen = fullscreen,
            CursorItems = new int[4],
            FadeCycle = fadeCycle,
            FadeClamp = fadeClamp,
            FadeAmount = fadeAmount,
            FadeInAmount = fadeIn,
            BlurRadius = blur,
            AllowedBindingString = allowedBinding,
            AllowedBinding = Inline(allowedBinding),
            SoundNameString = soundLoop,
            SoundName = Inline(soundLoop),
            ImageTrack = (int)ImageTrackType.UI,
            FocusColor = focusColor,
            VisibleStatement = visible,
            VisibleExpression = Inline(visible),
            RectXStatement = x, RectXExpression = Inline(x),
            RectYStatement = y, RectYExpression = Inline(y),
            RectWStatement = width, RectWExpression = Inline(width),
            RectHStatement = height, RectHExpression = Inline(height),
            OnOpenSet = open, OnOpen = Inline(open),
            OnCloseSet = close, OnClose = Inline(close),
            OnCloseRequestSet = requestClose, OnCloseRequest = Inline(requestClose),
            OnEscSet = escape, OnEsc = Inline(escape),
            ExecKeyHandler = handlers,
            ExecKeys = Inline(handlers),
            ItemCount = items.Count,
            ItemsPointer = items.Count == 0 ? default : new(-1),
            Items = items.ToArray(),
            ScaleTransitions = CreateTransitionSlots(),
            AlphaTransitions = CreateTransitionSlots(),
            XTransitions = CreateTransitionSlots(),
            YTransitions = CreateTransitionSlots()
        };
    }

    private static MenuTransition[] CreateTransitionSlots() => [new(), new(), new(), new()];

    private static XPointer<T> Inline<T>(T? value) where T : class => value is null ? default : new(-1);
}
