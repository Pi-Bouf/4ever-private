# Compiled data tables and strings (TChart.vcxproj)

add_library(TChart STATIC
    stdafx.cpp
    TChart.cpp
)
target_compile_definitions(TChart PRIVATE WIN32 NDEBUG)
target_include_directories(TChart
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}"
    PRIVATE
        "${TCLIENT_LIB_OWN}/TProtocol/Include"
)
target_link_libraries(TChart
    PUBLIC
        tclient_dxsdk
    PRIVATE
        tclient_options
)
