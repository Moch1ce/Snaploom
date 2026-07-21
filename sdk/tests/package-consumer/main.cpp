#include <snaploom/snaploom_capture.hpp>

int main() {
  snaploom::CaptureClient client;
  if (client.runtime_version().abi_major != snaploom::abi_major_v1 ||
      client.runtime_version().sdk_semver.empty()) {
    return 1;
  }
  client.close();
  return 0;
}
