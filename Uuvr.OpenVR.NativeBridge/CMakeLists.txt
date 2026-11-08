cmake_minimum_required(VERSION 3.24)

project(UuvrOpenVRNativeBridge LANGUAGES CXX)
set(UNITY_PLUGIN_HEADERS "" CACHE PATH "Unity native plugin SDK headers, supplied separately")

if(MSVC)
    add_compile_options(/utf-8 /W4)
endif()

add_library(UuvrOpenVRNativeBridge SHARED
    NativeBridge.cpp
)

target_compile_features(UuvrOpenVRNativeBridge PRIVATE cxx_std_20)
target_compile_definitions(UuvrOpenVRNativeBridge PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_include_directories(UuvrOpenVRNativeBridge PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/../vendor/openvr-official/headers"
    "${UNITY_PLUGIN_HEADERS}"
)
target_link_libraries(UuvrOpenVRNativeBridge PRIVATE
    d3d11 dxgi
    "${CMAKE_CURRENT_SOURCE_DIR}/../vendor/openvr-official/lib/win64/openvr_api.lib"
)

set_target_properties(UuvrOpenVRNativeBridge PROPERTIES
    OUTPUT_NAME "Uuvr.OpenVR.NativeBridge"
)

include(CTest)
if(BUILD_TESTING)
    add_executable(NativeBridgeTests NativeBridgeTests.cpp)
    target_compile_features(NativeBridgeTests PRIVATE cxx_std_20)
    target_compile_definitions(NativeBridgeTests PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX UUVR_BRIDGE_TEST)
    target_include_directories(NativeBridgeTests PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/../vendor/openvr-official/headers"
        "${UNITY_PLUGIN_HEADERS}")
    target_link_libraries(NativeBridgeTests PRIVATE d3d11 dxgi)
    add_test(NAME NativeBridgeLifecycle COMMAND NativeBridgeTests)
    set_tests_properties(NativeBridgeLifecycle PROPERTIES TIMEOUT 30)
endif()
