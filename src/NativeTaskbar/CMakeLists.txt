cmake_minimum_required(VERSION 3.25)
project(NativeTaskbar LANGUAGES CXX)

if(NOT WIN32 OR NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Use MSVC targeting x64 on Windows.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
add_compile_definitions(UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
add_compile_options(/W4 /permissive- /EHsc)

add_executable(NativeTaskbar Main.cpp)
target_include_directories(NativeTaskbar SYSTEM PRIVATE
    "${CMAKE_WINDOWS_KITS_10_DIR}/Include/${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}/cppwinrt")
target_link_libraries(NativeTaskbar PRIVATE user32 ole32 shell32 windowsapp)
add_library(NativeTaskbar.Xaml SHARED Diagnostics.cpp TaskbarStyle.cpp Diagnostics.def)
set_target_properties(NativeTaskbar.Xaml PROPERTIES UNITY_BUILD ON)
target_include_directories(NativeTaskbar.Xaml SYSTEM PRIVATE
    "${CMAKE_WINDOWS_KITS_10_DIR}/Include/${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}/cppwinrt")
target_link_libraries(NativeTaskbar.Xaml PRIVATE windowsapp ole32 oleaut32 user32)
add_dependencies(NativeTaskbar NativeTaskbar.Xaml)
