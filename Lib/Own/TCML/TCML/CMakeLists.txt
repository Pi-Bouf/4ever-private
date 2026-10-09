# TCML UI markup parser (TCML.vcxproj)

add_library(TCML STATIC
    cinterf.cpp
    TCML.cpp
    TCML.l.cpp
    TCMLParser.cpp
)
target_compile_definitions(TCML PRIVATE WIN32 NDEBUG _LIB)
target_compile_options(TCML PRIVATE /GS-)
target_include_directories(TCML PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(TCML PRIVATE tclient_options)
