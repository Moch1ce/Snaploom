cmake_minimum_required(VERSION 3.24)
project(SnaploomCaptureArchiveConsumer LANGUAGES C CXX)

enable_testing()
find_package(SnaploomCapture 0.1.0 CONFIG REQUIRED)

add_executable(snaploom_archive_c main.c)
target_link_libraries(snaploom_archive_c PRIVATE Snaploom::Capture)
set_target_properties(snaploom_archive_c PROPERTIES
  C_STANDARD 17 C_STANDARD_REQUIRED YES C_EXTENSIONS NO)

add_executable(snaploom_archive_cpp main.cpp)
target_link_libraries(snaploom_archive_cpp PRIVATE Snaploom::CaptureCpp)
set_target_properties(snaploom_archive_cpp PROPERTIES
  CXX_STANDARD 17 CXX_STANDARD_REQUIRED YES CXX_EXTENSIONS NO)

if(MSVC)
  target_compile_options(snaploom_archive_c PRIVATE /W4 /WX)
  target_compile_options(snaploom_archive_cpp PRIVATE /W4 /WX /EHsc)
else()
  target_compile_options(snaploom_archive_c PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(snaploom_archive_cpp PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

foreach(consumer snaploom_archive_c snaploom_archive_cpp)
  add_custom_command(TARGET ${consumer} POST_BUILD
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different
      "$<TARGET_FILE:Snaploom::Capture>" "$<TARGET_FILE_DIR:${consumer}>")
  if(APPLE)
    set_target_properties(${consumer} PROPERTIES BUILD_RPATH "@loader_path")
  endif()
  add_test(NAME ${consumer} COMMAND ${consumer})
endforeach()
