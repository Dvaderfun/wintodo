// Real desktop Share source; built on the host, executed only in WintodoTest.
#include <windows.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <winrt/Windows.ApplicationModel.DataTransfer.h>
#include <winrt/Windows.Foundation.h>
#include <fstream>
#include <sstream>
#include <thread>

using namespace winrt;
using namespace Windows::ApplicationModel::DataTransfer;
std::wstring fixtureDirectory;
std::wstring mode;
DataTransferManager manager{ nullptr };

std::wstring readFile(wchar_t const* name) {
    std::ifstream input(fixtureDirectory + L"\\" + name, std::ios::binary);
    std::ostringstream bytes;
    bytes << input.rdbuf();
    return to_hstring(bytes.str()).c_str();
}

LRESULT CALLBACK windowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_COMMAND && LOWORD(wParam) == 100) {
        try {
            SetForegroundWindow(window);
            auto interop = get_activation_factory<DataTransferManager, IDataTransferManagerInterop>();
            interop->ShowShareUIForWindow(window);
        } catch (hresult_error const& error) {
            std::ofstream(fixtureDirectory + L"\\source-error.txt") << to_string(error.message());
        }
    }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProc(window, message, wParam, lParam);
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    init_apartment(apartment_type::single_threaded);
    int count;
    auto arguments = CommandLineToArgvW(GetCommandLineW(), &count);
    if (count != 3) return 2;
    mode = arguments[1]; fixtureDirectory = arguments[2]; LocalFree(arguments);
    // Capture OutputDebugString from the broker-activated target without attaching a debugger.
    std::thread([] {
        auto bufferReady = CreateEvent(nullptr, FALSE, FALSE, L"DBWIN_BUFFER_READY");
        auto dataReady = CreateEvent(nullptr, FALSE, FALSE, L"DBWIN_DATA_READY");
        auto mapping = CreateFileMapping(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, 4096, L"DBWIN_BUFFER");
        auto data = static_cast<char*>(MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, 4096));
        if (!bufferReady || !dataReady || !data) return;
        for (;;) {
            SetEvent(bufferReady);
            if (WaitForSingleObject(dataReady, 1000) != WAIT_OBJECT_0) continue;
            std::ofstream(fixtureDirectory + L"\\debug-output.txt", std::ios::app) << *reinterpret_cast<DWORD*>(data) << ":" << (data + sizeof(DWORD)) << "\n";
        }
    }).detach();
    WNDCLASS windowClass{};
    windowClass.hInstance = instance;
    windowClass.lpfnWndProc = windowProc;
    windowClass.lpszClassName = L"WintodoShareSource";
    windowClass.hCursor = LoadCursor(nullptr, IDC_ARROW);
    windowClass.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
    RegisterClass(&windowClass);
    auto window = CreateWindow(windowClass.lpszClassName, L"Wintodo Share Source", WS_OVERLAPPEDWINDOW,
        100, 100, 640, 400, nullptr, nullptr, instance, nullptr);
    auto interop = get_activation_factory<DataTransferManager, IDataTransferManagerInterop>();
    check_hresult(interop->GetForWindow(window, guid_of<DataTransferManager>(), put_abi(manager)));
    manager.DataRequested([](auto const&, DataRequestedEventArgs const& args) {
        auto data = args.Request().Data();
        data.Properties().Title(readFile(L"title.txt"));
        if (mode == L"bad-provider") {
            data.SetDataProvider(StandardDataFormats::Text(), [](auto const& request) { request.SetData(box_value(42)); });
        } else if (mode == L"unsupported") {
            data.SetData(L"Wintodo.Unsupported", box_value(L"unsupported"));
        } else {
            if (mode != L"link") data.SetText(readFile(L"text.txt"));
            if (mode == L"link" || mode == L"combined")
                data.SetWebLink(Windows::Foundation::Uri(readFile(L"link.txt")));
        }
        data.ShareCompleted([](auto const&, auto const&) {
            std::ofstream(fixtureDirectory + L"\\completed.txt") << "completed";
        });
    });
    // The gate starts helpers hidden; the first ShowWindow honors STARTUPINFO.
    // A second call explicitly displays the fixture in the guest desktop.
    ShowWindow(window, SW_SHOW); ShowWindow(window, SW_SHOW); UpdateWindow(window);
    SetForegroundWindow(window);
    CreateWindow(L"BUTTON", L"Share fixture", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
        24, 24, 180, 40, window, reinterpret_cast<HMENU>(100), instance, nullptr);
    MSG message;
    while (GetMessage(&message, nullptr, 0, 0)) { TranslateMessage(&message); DispatchMessage(&message); }
    manager = nullptr;
    return 0;
}
