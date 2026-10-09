# Settings shared by every client target. They mirror the Release|Win32 configuration of the
# .vcxproj files (static MFC, MBCS, /MT, CP949 execution charset), so the CMake build produces
# the same code as the MSBuild one.

set(TCLIENT_ROOT "${CMAKE_CURRENT_LIST_DIR}/../..")
cmake_path(NORMAL_PATH TCLIENT_ROOT)

set(TCLIENT_LIB_OWN "${TCLIENT_ROOT}/Lib/Own")
set(TCLIENT_LIB_3RD "${TCLIENT_ROOT}/Lib/3rdParty")
set(TCLIENT_DXSDK "${TCLIENT_LIB_3RD}/DirectX9 (June 2010)")

# Static MFC (UseOfMfc=Static). Only the Visual Studio generators read this; with Ninja the
# MFC headers pick the static libraries themselves through #pragma comment(lib).
set(CMAKE_MFC_FLAG 1)

# /MT everywhere (RuntimeLibrary=MultiThreaded), static MFC requires it.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# With Ninja, CMake scans .rc dependencies by running the C compiler with the RC flags, and
# clang-cl rejects rc-only options such as /l0x0412. Let rc.exe alone see them.
set(CMAKE_NINJA_CMCLDEPS_RC OFF)

# PDBs for every configuration (DebugInformationFormat=ProgramDatabase).
set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "ProgramDatabase")

# Replace the CMake defaults: no implicit WIN32/_WINDOWS/NDEBUG, every target states its own
# defines like the .vcxproj does.
set(CMAKE_CXX_FLAGS "/EHsc")
set(CMAKE_C_FLAGS "")
set(CMAKE_CXX_FLAGS_RELEASE "/O2")
set(CMAKE_C_FLAGS_RELEASE "/O2")
set(CMAKE_CXX_FLAGS_DEBUG "/Od /RTC1")
set(CMAKE_C_FLAGS_DEBUG "/Od /RTC1")

add_library(tclient_options INTERFACE)
target_compile_definitions(tclient_options INTERFACE _MBCS)
target_compile_options(tclient_options INTERFACE
    /source-charset:utf-8
    /W3
    /GF
    /Oy-
    /Zc:inline
    /Zc:wchar_t
    /Zc:forScope
    /fp:precise
)
# Narrow string literals are CP949 for MSVC. clang-cl only supports UTF-8, so the few non-ASCII
# literals that reach the game are written as CP949 \x escapes (same bytes with both compilers);
# only TRACE/ASSERT texts and comments are left as UTF-8.
if(CMAKE_CXX_COMPILER_ID STREQUAL "MSVC")
    target_compile_options(tclient_options INTERFACE /execution-charset:.949)
else()
    # -ferror-limit=0: report every error of a file, not only the first 20.
    target_compile_options(tclient_options INTERFACE /execution-charset:utf-8 -ferror-limit=0)
endif()

# Ninja runs one compiler per file; MSBuild needs /MP to compile files in parallel.
if(CMAKE_GENERATOR MATCHES "Visual Studio")
    target_compile_options(tclient_options INTERFACE /MP)
endif()

# Old DirectX SDK headers (d3dx9, dsound, ...). They must come AFTER the Windows SDK headers,
# whose newer dxgi/d3d headers they would otherwise shadow (MSBuild: IncludePath=$(IncludePath);DX).
add_library(tclient_dxsdk INTERFACE)
target_link_directories(tclient_dxsdk INTERFACE "${TCLIENT_DXSDK}/Lib/x86")
if(CMAKE_GENERATOR MATCHES "Visual Studio")
    set(CMAKE_VS_SDK_INCLUDE_DIRECTORIES "$(VC_IncludePath);$(WindowsSDK_IncludePath);${TCLIENT_DXSDK}/Include")
else()
    # Command-line /I paths are searched before the INCLUDE variable of the developer shell, so
    # repeat the shell's directories first, then the DirectX SDK.
    set(_tclient_env_include "$ENV{INCLUDE}")
    list(FILTER _tclient_env_include EXCLUDE REGEX "^$")
    target_include_directories(tclient_dxsdk SYSTEM INTERFACE ${_tclient_env_include} "${TCLIENT_DXSDK}/Include")
endif()
