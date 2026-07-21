#include "snaploom_capture.h"

#include <stdatomic.h>
#include <string.h>

#if defined(_WIN32)
#include <windows.h>
#else
#include <unistd.h>
#endif

typedef struct harness_state {
  atomic_int callbacks;
  atomic_int valid;
} harness_state;

static void SNAPLOOM_CALL harness_callback(
    snaploom_capture_client_v1 *client,
    snaploom_capture_completion_v1 *completion,
    void *user_data) {
  harness_state *state = (harness_state *)user_data;
  (void)client;
  if (completion != NULL &&
      completion->struct_size == sizeof(snaploom_capture_completion_v1) &&
      completion->kind == SNAPLOOM_COMPLETION_COMPLETED &&
      completion->png_data != NULL && completion->png_size > 0 &&
      completion->pixel_width == 1 && completion->pixel_height == 1) {
    atomic_store(&state->valid, 1);
  }
  atomic_fetch_add(&state->callbacks, 1);
  snaploom_capture_completion_free_v1(completion);
}

int snaploom_c_harness_run(void) {
  snaploom_capture_version_info_v1 version = {0};
  snaploom_capture_client_v1 *client = NULL;
  snaploom_request_id_v1 request_id = 0;
  harness_state state;
  int attempts;
  atomic_init(&state.callbacks, 0);
  atomic_init(&state.valid, 0);
  version.struct_size = sizeof(version);

  if (snaploom_capture_version_v1(&version) != SNAPLOOM_STATUS_OK ||
      version.abi_major != 1 || version.sdk_semver.data == NULL ||
      version.sdk_semver.length == 0) {
    return 1;
  }
  if (snaploom_capture_client_create_v1(NULL, &client) != SNAPLOOM_STATUS_OK ||
      client == NULL) {
    return 2;
  }
  if (snaploom_capture_start_v1(client, NULL, harness_callback, &state,
                                &request_id) != SNAPLOOM_STATUS_OK ||
      request_id == 0) {
    return 3;
  }
  for (attempts = 0; attempts < 1000 && atomic_load(&state.callbacks) == 0;
       ++attempts) {
#if defined(_WIN32)
    Sleep(1);
#else
    usleep(1000);
#endif
  }
  if (atomic_load(&state.callbacks) != 1 || atomic_load(&state.valid) != 1) {
    return 4;
  }
  if (snaploom_capture_client_destroy_v1(client) != SNAPLOOM_STATUS_OK) {
    return 5;
  }
  return 0;
}
