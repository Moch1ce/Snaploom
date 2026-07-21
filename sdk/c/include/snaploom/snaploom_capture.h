#ifndef SNAPLOOM_CAPTURE_H
#define SNAPLOOM_CAPTURE_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define SNAPLOOM_CALL __cdecl
#if defined(SNAPLOOM_CAPTURE_BUILD)
#define SNAPLOOM_API __declspec(dllexport)
#else
#define SNAPLOOM_API __declspec(dllimport)
#endif
#else
#define SNAPLOOM_CALL
#define SNAPLOOM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef uint32_t snaploom_status_v1;
typedef uint32_t snaploom_completion_kind_v1;
typedef uint32_t snaploom_error_v1;
typedef uint64_t snaploom_request_id_v1;
typedef struct snaploom_capture_client_v1 snaploom_capture_client_v1;

enum {
  SNAPLOOM_STATUS_OK = 0,
  SNAPLOOM_STATUS_INVALID_ARGUMENT = 1,
  SNAPLOOM_STATUS_INVALID_STRUCT_SIZE = 2,
  SNAPLOOM_STATUS_CLIENT_CLOSED = 3,
  SNAPLOOM_STATUS_CALLBACK_CONTEXT = 4,
  SNAPLOOM_STATUS_NOT_FOUND = 5,
  SNAPLOOM_STATUS_OUT_OF_MEMORY = 6,
  SNAPLOOM_STATUS_INTERNAL = 255
};

enum {
  SNAPLOOM_COMPLETION_UNSPECIFIED = 0,
  SNAPLOOM_COMPLETION_COMPLETED = 1,
  SNAPLOOM_COMPLETION_CANCELED = 2,
  SNAPLOOM_COMPLETION_FAILED = 3
};

enum {
  SNAPLOOM_ERROR_NONE = 0,
  SNAPLOOM_ERROR_BUSY = 1,
  SNAPLOOM_ERROR_HOST_NOT_FOUND = 2,
  SNAPLOOM_ERROR_HOST_START_FAILED = 3,
  SNAPLOOM_ERROR_HOST_START_TIMEOUT = 4,
  SNAPLOOM_ERROR_AUTHENTICATION_FAILED = 5,
  SNAPLOOM_ERROR_PROTOCOL_INCOMPATIBLE = 6,
  SNAPLOOM_ERROR_PROTOCOL_ERROR = 7,
  SNAPLOOM_ERROR_HOST_CRASHED = 8,
  SNAPLOOM_ERROR_TRANSPORT_FAILED = 9,
  SNAPLOOM_ERROR_HANDSHAKE_TIMEOUT = 10,
  SNAPLOOM_ERROR_REQUEST_TIMEOUT = 11,
  SNAPLOOM_ERROR_PLATFORM_UNAVAILABLE = 20,
  SNAPLOOM_ERROR_PERMISSION_NOT_GRANTED = 21,
  SNAPLOOM_ERROR_PERMISSION_REVOKED = 22,
  SNAPLOOM_ERROR_DISPLAY_UNAVAILABLE = 23,
  SNAPLOOM_ERROR_CAPTURE_UNAVAILABLE = 24,
  SNAPLOOM_ERROR_CAPTURE_TIMEOUT = 25,
  SNAPLOOM_ERROR_PIXEL_CONVERSION_FAILED = 26,
  SNAPLOOM_ERROR_INVALID_RESULT = 30,
  SNAPLOOM_ERROR_RESULT_TOO_LARGE = 31,
  SNAPLOOM_ERROR_CLIPBOARD_WRITE_FAILED = 32,
  SNAPLOOM_ERROR_OUT_OF_MEMORY = 40,
  SNAPLOOM_ERROR_CLIENT_CLOSED = 41,
  SNAPLOOM_ERROR_INTERNAL = 255
};

#define SNAPLOOM_CAPTURE_FLAG_DISABLE_CLIPBOARD UINT32_C(1)
#define SNAPLOOM_COMPLETION_FLAG_CLIPBOARD_WRITTEN UINT32_C(1)
#define SNAPLOOM_COMPLETION_FLAG_RETRYABLE UINT32_C(2)

typedef struct snaploom_utf8_view_v1 {
  const uint8_t *data;
  uint64_t length;
} snaploom_utf8_view_v1;

typedef struct snaploom_capture_version_info_v1 {
  uint32_t struct_size;
  uint32_t abi_major;
  snaploom_utf8_view_v1 sdk_semver;
  uint64_t reserved[4];
} snaploom_capture_version_info_v1;

typedef struct snaploom_capture_client_config_v1 {
  uint32_t struct_size;
  uint32_t flags;
  snaploom_utf8_view_v1 host_executable_override;
  uint32_t launch_timeout_ms;
  uint32_t handshake_timeout_ms;
  uint64_t reserved[4];
} snaploom_capture_client_config_v1;

typedef struct snaploom_capture_options_v1 {
  uint32_t struct_size;
  uint32_t flags;
  uint64_t interaction_timeout_ms;
  uint64_t reserved[4];
} snaploom_capture_options_v1;

#define SNAPLOOM_CAPTURE_CLIENT_CONFIG_V1_INIT                         \
  {                                                                   \
    sizeof(snaploom_capture_client_config_v1), 0, { NULL, 0 }, 0, 0, \
        { 0, 0, 0, 0 }                                                \
  }
#define SNAPLOOM_CAPTURE_OPTIONS_V1_INIT \
  { sizeof(snaploom_capture_options_v1), 0, 0, { 0, 0, 0, 0 } }

typedef struct snaploom_capture_completion_v1 {
  uint32_t struct_size;
  uint32_t kind;
  uint32_t error_code;
  uint32_t flags;
  uint64_t request_id;
  const uint8_t *png_data;
  uint64_t png_size;
  uint32_t pixel_width;
  uint32_t pixel_height;
  uint64_t reserved[4];
} snaploom_capture_completion_v1;

typedef void (SNAPLOOM_CALL *snaploom_capture_callback_v1)(
    snaploom_capture_client_v1 *client,
    snaploom_capture_completion_v1 *completion,
    void *user_data);

SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_version_v1(snaploom_capture_version_info_v1 *out_version);
SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_client_create_v1(
    const snaploom_capture_client_config_v1 *config,
    snaploom_capture_client_v1 **out_client);
SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_start_v1(
    snaploom_capture_client_v1 *client,
    const snaploom_capture_options_v1 *options,
    snaploom_capture_callback_v1 callback,
    void *user_data,
    snaploom_request_id_v1 *out_request_id);
SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_cancel_v1(
    snaploom_capture_client_v1 *client,
    snaploom_request_id_v1 request_id);
SNAPLOOM_API snaploom_status_v1 SNAPLOOM_CALL
snaploom_capture_client_destroy_v1(snaploom_capture_client_v1 *client);
SNAPLOOM_API void SNAPLOOM_CALL
snaploom_capture_completion_free_v1(snaploom_capture_completion_v1 *completion);
SNAPLOOM_API const char *SNAPLOOM_CALL
snaploom_capture_error_name_v1(snaploom_error_v1 error_code);

#ifdef __cplusplus
}
#endif

#endif
