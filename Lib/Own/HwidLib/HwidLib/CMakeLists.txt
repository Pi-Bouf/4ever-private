# Hardware-ID fingerprinting (HwidLib.vcxproj)

add_library(HwidLib STATIC
    __CpuInfo.cpp
    __MotherboardInfo.cpp
    __Sha256.cpp
    __SysCommand.cpp
    __WindowsInfo.cpp
    HwidManager.cpp
    stdafx.cpp
)
target_compile_definitions(HwidLib PRIVATE WIN32 NDEBUG _LIB)
target_include_directories(HwidLib PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(HwidLib PRIVATE tclient_options)
