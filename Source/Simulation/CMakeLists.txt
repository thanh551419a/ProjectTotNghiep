cmake_minimum_required(VERSION 3.16)
project(SimulationNative LANGUAGES CXX)

add_library(SimulationNative STATIC
    SimulationApi.cpp
)

target_include_directories(SimulationNative
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/..
)

target_compile_features(SimulationNative PUBLIC cxx_std_17)

add_executable(SimulationNativeTest
    SimulationApiTest.cpp
)

target_link_libraries(SimulationNativeTest PRIVATE SimulationNative)

target_compile_features(SimulationNativeTest PRIVATE cxx_std_17)
