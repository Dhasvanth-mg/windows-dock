cmake_minimum_required(VERSION 3.25)
project(NativeTaskbar LANGUAGES CXX RC)

if(NOT WIN32 OR NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Use MSVC targeting x64 on Windows.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
add_compile_definitions(UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
add_compile_options(/W4 /permissive- /EHsc)

set(DOCK_ICON "${CMAKE_CURRENT_SOURCE_DIR}/../Shared/DockIcon.ico")
configure_file(DockIcon.rc.in DockIcon.rc @ONLY)
add_executable(NativeTaskbar Main.cpp DockTray.cpp "${CMAKE_CURRENT_BINARY_DIR}/DockIcon.rc")
target_include_directories(NativeTaskbar SYSTEM PRIVATE
    "${CMAKE_WINDOWS_KITS_10_DIR}/Include/${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}/cppwinrt")
target_link_libraries(NativeTaskbar PRIVATE user32 gdi32 ole32 shell32 windowsapp)
add_library(NativeTaskbar.Xaml SHARED Diagnostics.cpp EmbeddedTaskbar.cpp TaskbarStyle.cpp TaskbarAgents.cpp Diagnostics.def
    "${CMAKE_CURRENT_BINARY_DIR}/DockIcon.rc")
set_target_properties(NativeTaskbar.Xaml PROPERTIES UNITY_BUILD ON)
set(AGENT_FACE_SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/../Shared/AgentFaces.txt")
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${AGENT_FACE_SOURCE}")
file(READ "${AGENT_FACE_SOURCE}" AGENT_FACE_ART)
configure_file(AgentFaces.h.in AgentFaces.h @ONLY)
target_include_directories(NativeTaskbar.Xaml PRIVATE "${CMAKE_CURRENT_BINARY_DIR}")
set(NATIVE_TASKBAR_MODULE_NAME "NativeTaskbar.Xaml" CACHE STRING "Output name of the XAML diagnostics DLL")
set_target_properties(NativeTaskbar.Xaml PROPERTIES OUTPUT_NAME "${NATIVE_TASKBAR_MODULE_NAME}")
target_compile_definitions(NativeTaskbar PRIVATE NATIVE_TASKBAR_DLL=L"${NATIVE_TASKBAR_MODULE_NAME}.dll")
target_include_directories(NativeTaskbar.Xaml SYSTEM PRIVATE
    "${CMAKE_WINDOWS_KITS_10_DIR}/Include/${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}/cppwinrt")
target_link_libraries(NativeTaskbar.Xaml PRIVATE windowsapp ole32 oleaut32 user32 gdi32)
add_dependencies(NativeTaskbar NativeTaskbar.Xaml)
