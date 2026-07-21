#include <snaploom/snaploom_capture.h>

#include <string.h>

int main(void) {
  snaploom_capture_version_info_v1 version = {0};
  version.struct_size = sizeof(version);
  if (snaploom_capture_version_v1(&version) != SNAPLOOM_STATUS_OK ||
      version.abi_major != 1 || version.sdk_semver.data == NULL ||
      version.sdk_semver.length == 0) {
    return 1;
  }

  snaploom_capture_client_v1* client = NULL;
  if (snaploom_capture_client_create_v1(NULL, &client) != SNAPLOOM_STATUS_OK ||
      client == NULL) {
    return 2;
  }
  return snaploom_capture_client_destroy_v1(client) == SNAPLOOM_STATUS_OK ? 0
                                                                          : 3;
}
