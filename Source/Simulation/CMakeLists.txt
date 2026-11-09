cmake_minimum_required(VERSION 3.16)
project(SpikeCrossCore LANGUAGES CXX)
include(CTest)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(SPIKE_DEPLOY_TO_UNITY "Copy the built native library into the Unity project's Plugins folder" ON)

set(SIM_SOURCES
    ${CMAKE_CURRENT_SOURCE_DIR}/SpikeNativeApi.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/SimulationApi.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/SimulationRuntime.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/Diagnostics/SimSnapshotJson.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/Diagnostics/SimDiagnostics.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../Input/InputMapping.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../ECSCore/ComponentStorage/ComponentStorage.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/FramePreparationSystem.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/PlayerInputSystem.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/Player2InputSystem.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/AIInputSystem.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/UpdateCharacterActionStateSystem.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/JumpStartSystem.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/UpdateTrajectory.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/VelocityIntentSystem.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/GameRuleEvaluationSystem.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/LoadStatsData.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/Trajectory/TrajectoryMath.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/Trajectory/TrajectoryUpdate.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/Trajectory/RallyStateUtils.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/Trajectory/UpdateTrajectoryFromIntent.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../System/Trajectory/UpdateTrajectoryByCollision.cpp
)

# 0. One compilation of the simulation core, shared by the shared library and the
#    desktop tools (so the runner/tests execute exactly the code that ships).
add_library(SpikeSimObjects OBJECT ${SIM_SOURCES})
set_target_properties(SpikeSimObjects PROPERTIES POSITION_INDEPENDENT_CODE ON)
target_include_directories(SpikeSimObjects
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/..
        ${CMAKE_CURRENT_SOURCE_DIR}/../ThirdParty
)
target_compile_definitions(SpikeSimObjects
    PUBLIC
        SIM_BUILD_DLL
        _CRT_SECURE_NO_WARNINGS
)

# 1. Native Shared Library / DLL
add_library(SpikeCrossCore SHARED $<TARGET_OBJECTS:SpikeSimObjects>)

target_include_directories(SpikeCrossCore
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/..
        ${CMAKE_CURRENT_SOURCE_DIR}/../ThirdParty
)

if (ANDROID)
    target_link_libraries(SpikeCrossCore PRIVATE log c++_static c++abi)
    set(UNITY_PLUGINS_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../Unity/TheSpike/My project/Assets/Plugins/Android/libs/${ANDROID_ABI}")
else()
    # 2. Standalone C++ Regression & Verification Test (Desktop only)
    add_executable(SimulationNativeTest
        ${CMAKE_CURRENT_SOURCE_DIR}/SimulationNativeTest.cpp
    )

    target_include_directories(SimulationNativeTest
        PRIVATE
            ${CMAKE_CURRENT_SOURCE_DIR}
            ${CMAKE_CURRENT_SOURCE_DIR}/..
    )

    target_link_libraries(SimulationNativeTest PRIVATE SpikeCrossCore)

    # 3. Headless diagnostics runner (scenarios + JSONL instrumentation), same core objects
    add_executable(SimDiagRunner
        ${CMAKE_CURRENT_SOURCE_DIR}/Tools/SimDiagRunner.cpp
        $<TARGET_OBJECTS:SpikeSimObjects>
    )
    target_include_directories(SimDiagRunner PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/.. ${CMAKE_CURRENT_SOURCE_DIR}/../ThirdParty)
    target_compile_definitions(SimDiagRunner PRIVATE SIM_BUILD_DLL _CRT_SECURE_NO_WARNINGS)

    # 4. Snapshot coverage test (serialized JSON vs original storage), same core objects
    add_executable(SimSnapshotCoverageTest
        ${CMAKE_CURRENT_SOURCE_DIR}/Tools/SimSnapshotCoverageTest.cpp
        $<TARGET_OBJECTS:SpikeSimObjects>
    )
    target_include_directories(SimSnapshotCoverageTest PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/.. ${CMAKE_CURRENT_SOURCE_DIR}/../ThirdParty)
    target_compile_definitions(SimSnapshotCoverageTest PRIVATE SIM_BUILD_DLL _CRT_SECURE_NO_WARNINGS)

    # 5. Architecture Verification Test for Get_data
    add_executable(SimulationArchTest
        ${CMAKE_CURRENT_SOURCE_DIR}/SimulationArchTest.cpp
        $<TARGET_OBJECTS:SpikeSimObjects>
    )
    target_include_directories(SimulationArchTest PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/.. ${CMAKE_CURRENT_SOURCE_DIR}/../ThirdParty)
    target_compile_definitions(SimulationArchTest PRIVATE SIM_BUILD_DLL _CRT_SECURE_NO_WARNINGS)

    # Isolated local-ECS input contract tests; this does not mutate SimulationRuntime.
    add_executable(P02InputContractTest
        ${CMAKE_CURRENT_SOURCE_DIR}/P02InputContractTest.cpp
        $<TARGET_OBJECTS:SpikeSimObjects>
    )
    target_include_directories(P02InputContractTest PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/.. ${CMAKE_CURRENT_SOURCE_DIR}/../ThirdParty)
    target_compile_definitions(P02InputContractTest PRIVATE SIM_BUILD_DLL _CRT_SECURE_NO_WARNINGS)

    if (BUILD_TESTING)
        set(P02_BUTTON_MAPPING_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../../../Unity/TheSpike/My project/Assets/StreamingAssets/button_mapping.json")
        add_test(NAME P02InputContractTest COMMAND P02InputContractTest)
        add_test(NAME P02GetDataInputTest COMMAND P02InputContractTest --get-data "${P02_BUTTON_MAPPING_PATH}")
        add_test(NAME P02ConfiguredSpikeAcceptance COMMAND P02InputContractTest --spikes "${P02_BUTTON_MAPPING_PATH}")
        set_tests_properties(P02ConfiguredSpikeAcceptance PROPERTIES SKIP_RETURN_CODE 77)
    endif()

    set(UNITY_PLUGINS_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../Unity/TheSpike/My project/Assets/Plugins/x86_64")
endif()

# 5. Post-build step: Deploy shared library to Unity Plugins
if (SPIKE_DEPLOY_TO_UNITY)
    add_custom_command(TARGET SpikeCrossCore POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E make_directory "${UNITY_PLUGINS_DIR}"
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "$<TARGET_FILE:SpikeCrossCore>" "${UNITY_PLUGINS_DIR}/$<TARGET_FILE_NAME:SpikeCrossCore>"
        COMMENT "Deploying $<TARGET_FILE_NAME:SpikeCrossCore> to ${UNITY_PLUGINS_DIR}"
    )
endif()
