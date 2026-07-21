#include <snaploom/snaploom_capture.hpp>

#include <iostream>

int main() {
  snaploom::CaptureClient client;
  std::cout << "Snaploom Capture ABI " << client.runtime_version().abi_major
            << " SDK " << client.runtime_version().sdk_semver << '\n';
  client.close();
  return 0;
}
