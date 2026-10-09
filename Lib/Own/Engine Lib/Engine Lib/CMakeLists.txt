# Tachyon 3D engine (Engine Lib.vcxproj)

set(EngineLib_SOURCES
    CryptographyExt.cpp
    D3DCamera.cpp
    D3DDevice.cpp
    D3DFont.cpp
    D3DImage.cpp
    D3DLight.cpp
    D3DSound.cpp
    FrameGroup.cpp
    Packet.cpp
    StdAfx.cpp
    T3DMusic.cpp
    T3DTexture.cpp
    TachyonAnimation.cpp
    TachyonApp.cpp
    TachyonBSPMAP.cpp
    TachyonCompressor.cpp
    TachyonEXTMAP.cpp
    TachyonHUGEMAP.cpp
    TachyonMAP.cpp
    TachyonMedia.cpp
    TachyonMesh.cpp
    TachyonObject.cpp
    TachyonRes.cpp
    TachyonSession.cpp
    TachyonSFX.cpp
    TachyonSlashSFX.cpp
    TachyonWnd.cpp
    TBSPNode.cpp
    TBSPPortal.cpp
    TConsole.cpp
    TDynamicBillboard.cpp
    TMath.cpp
    TMiniAudio.cpp
    TModuleProtector.cpp
    TNICEventSink.cpp
    TProfile.cpp
    TShow.cpp
)

add_library(EngineLib STATIC ${EngineLib_SOURCES})
target_compile_definitions(EngineLib PRIVATE WIN32 NDEBUG _WINDOWS)
target_compile_options(EngineLib PRIVATE /GS-)
target_include_directories(EngineLib
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}"
        "${TCLIENT_LIB_3RD}/zlib/include"
    PRIVATE
        "${TCLIENT_LIB_OWN}/GameConfig"
        "${TCLIENT_LIB_3RD}/miniaudio"
        # TComp and Engine Lib include each other's headers.
        "${TCLIENT_LIB_OWN}/TComp/TComp"
)
target_link_directories(EngineLib PUBLIC "${TCLIENT_LIB_3RD}/zlib/lib/x86")
target_link_libraries(EngineLib
    PUBLIC
        tclient_dxsdk
        TCML
        zlibstatic
    PRIVATE
        tclient_options
)
