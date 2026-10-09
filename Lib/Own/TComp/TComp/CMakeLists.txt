# UI widget toolkit built on TCML (TComp.vcxproj)

add_library(TComp STATIC
    GDIFont.cpp
    StdAfx.cpp
    TButton.cpp
    TCombo.cpp
    TComp.cpp
    TComponent.cpp
    TEdit.cpp
    TFrame.cpp
    TGauge.cpp
    THtmlDocWnd.cpp
    THttpCtrl.cpp
    TImageList.cpp
    TList.cpp
    TListItem.cpp
    TMeter.cpp
    TMultiLineEdit.cpp
    TOccManager.cpp
    TScroll.cpp
    TTabCtrl.cpp
    TToolTip.cpp
)
target_compile_definitions(TComp PRIVATE _HAS_AUTO_PTR_ETC=1 WIN32 NDEBUG _WINDOWS)
target_compile_options(TComp PRIVATE /GS-)
target_include_directories(TComp PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(TComp
    PUBLIC
        TCML
        EngineLib
    PRIVATE
        tclient_options
)
