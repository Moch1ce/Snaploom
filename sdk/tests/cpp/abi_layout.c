#include <snaploom/snaploom_capture.h>

#include <stddef.h>

_Static_assert(sizeof(snaploom_utf8_view_v1) == 16, "UTF-8 view ABI drift");
_Static_assert(sizeof(snaploom_capture_version_info_v1) == 56,
               "version ABI drift");
_Static_assert(sizeof(snaploom_capture_client_config_v1) == 64,
               "client config ABI drift");
_Static_assert(sizeof(snaploom_capture_options_v1) == 48,
               "capture options ABI drift");
_Static_assert(sizeof(snaploom_capture_completion_v1) == 80,
               "completion ABI drift");
_Static_assert(offsetof(snaploom_capture_completion_v1, request_id) == 16,
               "request ID offset drift");
_Static_assert(offsetof(snaploom_capture_completion_v1, png_data) == 24,
               "PNG pointer offset drift");
_Static_assert(offsetof(snaploom_capture_completion_v1, reserved) == 48,
               "completion reserved offset drift");

int main(void) {
  snaploom_capture_client_config_v1 config =
      SNAPLOOM_CAPTURE_CLIENT_CONFIG_V1_INIT;
  snaploom_capture_options_v1 options = SNAPLOOM_CAPTURE_OPTIONS_V1_INIT;
  return config.struct_size == sizeof(config) &&
                 options.struct_size == sizeof(options)
             ? 0
             : 1;
}
