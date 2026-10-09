# CMake build of the game client. It builds the same six projects as TClient.sln
# (TClient, Engine Lib, TChart, TCML, TComp, HwidLib). See BUILD-CMAKE.md.

cmake_minimum_required(VERSION 3.25)
project(TClient LANGUAGES C CXX RC)

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "The client is 32-bit only for now: configure with an x86 toolchain (-A Win32 or an x86 developer shell).")
endif()

include(cmake/TClientCommon.cmake)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

add_subdirectory("${TCLIENT_LIB_OWN}/TCML/TCML" TCML)
add_subdirectory("${TCLIENT_LIB_OWN}/Engine Lib/Engine Lib" EngineLib)
add_subdirectory("${TCLIENT_LIB_OWN}/TComp/TComp" TComp)
add_subdirectory("${TCLIENT_LIB_OWN}/TChart/TChart" TChart)
add_subdirectory("${TCLIENT_LIB_OWN}/HwidLib/HwidLib" HwidLib)
add_subdirectory(TClient)

# Copy TClient.exe into Game/ after each build, like the MSBuild project (OutDir=..\..\Game\).
option(TCLIENT_DEPLOY_TO_GAME "Copy TClient.exe into the Game folder after building" OFF)
if(TCLIENT_DEPLOY_TO_GAME)
    add_custom_command(TARGET TClient POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "$<TARGET_FILE:TClient>" "${TCLIENT_ROOT}/Game/TClient.exe"
        COMMENT "Deploying TClient.exe to Game/")
endif()
