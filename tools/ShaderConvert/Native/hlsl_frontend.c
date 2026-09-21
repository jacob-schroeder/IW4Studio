/* D3D9 HLSL frontend. The RSX lowering remains in MapConverter. */
typedef unsigned int UINT;
typedef unsigned long long SIZE_T;
typedef unsigned short WCHAR;
typedef long HRESULT;
typedef void *HANDLE;
#define DLL __declspec(dllimport)
DLL WCHAR *GetCommandLineW(void);
DLL WCHAR **CommandLineToArgvW(const WCHAR *, int *);
DLL HANDLE GetStdHandle(UINT);
DLL int WriteFile(HANDLE, const void *, UINT, UINT *, void *);
DLL HANDLE LoadLibraryW(const WCHAR *);
DLL void *GetProcAddress(HANDLE, const char *);
DLL UINT GetLastError(void);
DLL HANDLE CreateFileW(const WCHAR *, UINT, UINT, void *, UINT, UINT, HANDLE);
DLL int CloseHandle(HANDLE);
DLL void *LocalFree(void *);
DLL __declspec(noreturn) void ExitProcess(UINT);

typedef struct Blob Blob;
typedef struct BlobVTable {
    void *query;
    void *addref;
    UINT (*release)(Blob *);
    void *(*pointer)(Blob *);
    SIZE_T (*size)(Blob *);
} BlobVTable;
struct Blob { BlobVTable *v; };
typedef HRESULT (*CompileFile)(const WCHAR *, void *, void *, const char *, const char *, UINT, UINT, Blob **, Blob **);

static void error(const char *text) {
    UINT size = 0, written;
    while (text[size]) size++;
    WriteFile(GetStdHandle((UINT)-12), text, size, &written, 0);
}

static void error_code(UINT value) {
    char text[12] = "0x00000000\n";
    const char *digits = "0123456789abcdef";
    for (UINT n = 0; n < 8; n++) text[9 - n] = digits[(value >> (4 * n)) & 15];
    error(text);
}

static void ascii_argument(const WCHAR *source, char *destination, UINT capacity) {
    UINT n = 0;
    while (source[n]) {
        if (n + 1 >= capacity || source[n] > 127) {
            error("Entry point and profile must be ASCII and shorter than 128 characters.\n");
            ExitProcess(2);
        }
        destination[n] = (char)source[n];
        n++;
    }
    destination[n] = 0;
}

void entry(void) {
    int count = 0;
    WCHAR **arguments = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!arguments || count != 6) {
        error("Usage: shaderconvert-hlsl.exe input.hlsl output.cso entry profile d3dcompiler.dll\n");
        ExitProcess(2);
    }
    char entry_point[128], profile[128];
    ascii_argument(arguments[3], entry_point, sizeof(entry_point));
    ascii_argument(arguments[4], profile, sizeof(profile));
    HANDLE library = LoadLibraryW(arguments[5]);
    if (!library) {
        UINT code = GetLastError();
        error("Cannot load the selected D3DCompiler DLL: ");
        error_code(code);
        ExitProcess(3);
    }
    CompileFile compile = (CompileFile)GetProcAddress(library, "D3DCompileFromFile");
    if (!compile) {
        error("D3DCompileFromFile is unavailable.\n");
        ExitProcess(3);
    }
    Blob *program = 0, *messages = 0;
    /* Standard include handling; optimization level 3, D3D9 compatibility. */
    HRESULT result = compile(arguments[1], 0, (void *)1, entry_point, profile,
                             (1u << 15) | (1u << 12), 0, &program, &messages);
    if (messages) {
        UINT written;
        WriteFile(GetStdHandle((UINT)-12), messages->v->pointer(messages),
                  (UINT)messages->v->size(messages), &written, 0);
        messages->v->release(messages);
    }
    if (result < 0 || !program) {
        error("D3DCompile failed: ");
        error_code((UINT)result);
        ExitProcess(4);
    }
    HANDLE file = CreateFileW(arguments[2], 0x40000000, 0, 0, 2, 0x80, 0);
    if (file == (HANDLE)-1) {
        UINT code = GetLastError();
        error("Cannot open bytecode output: ");
        error_code(code);
        ExitProcess(5);
    }
    SIZE_T size = program->v->size(program);
    UINT written = 0;
    int success = size <= 0xffffffffu &&
        WriteFile(file, program->v->pointer(program), (UINT)size, &written, 0);
    CloseHandle(file);
    program->v->release(program);
    LocalFree(arguments);
    if (!success || written != size) {
        error("Cannot write complete bytecode output.\n");
        ExitProcess(5);
    }
    ExitProcess(0);
}
