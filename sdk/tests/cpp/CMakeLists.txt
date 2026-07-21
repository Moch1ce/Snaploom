cmake_minimum_required(VERSION 3.24)
project(SnaploomCaptureCppContract LANGUAGES C CXX)

option(SNAPLOOM_ENABLE_ASAN "Run the C++ ownership contract under AddressSanitizer" OFF)

enable_testing()

add_library(snaploom_public_headers INTERFACE)
target_include_directories(
  snaploom_public_headers
  INTERFACE
    "${CMAKE_CURRENT_LIST_DIR}/../../c/include"
    "${CMAKE_CURRENT_LIST_DIR}/../../cpp/include"
)

add_executable(snaploom_c17_layout abi_layout.c)
target_link_libraries(snaploom_c17_layout PRIVATE snaploom_public_headers)
set_target_properties(snaploom_c17_layout PROPERTIES C_STANDARD 17 C_STANDARD_REQUIRED YES C_EXTENSIONS NO)

add_executable(snaploom_cpp17_contract cpp_contract.cpp)
target_link_libraries(snaploom_cpp17_contract PRIVATE snaploom_public_headers)
set_target_properties(snaploom_cpp17_contract PROPERTIES CXX_STANDARD 17 CXX_STANDARD_REQUIRED YES CXX_EXTENSIONS NO)

if(MSVC)
  target_compile_options(snaploom_c17_layout PRIVATE /W4 /WX)
  target_compile_options(snaploom_cpp17_contract PRIVATE /W4 /WX /EHsc)
else()
  target_compile_options(snaploom_c17_layout PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(snaploom_cpp17_contract PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

if(SNAPLOOM_ENABLE_ASAN AND CMAKE_CXX_COMPILER_ID MATCHES "AppleClang|Clang")
  target_compile_options(snaploom_cpp17_contract PRIVATE -fsanitize=address -fno-omit-frame-pointer)
  target_link_options(snaploom_cpp17_contract PRIVATE -fsanitize=address)
endif()

add_test(NAME c17-layout COMMAND snaploom_c17_layout)
add_test(NAME cpp17-raii COMMAND snaploom_cpp17_contract)
