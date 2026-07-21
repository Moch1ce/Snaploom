#define SNAPLOOM_CAPTURE_BUILD
#include <snaploom/snaploom_capture.hpp>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <map>
#include <thread>
#include <type_traits>

struct snaploom_capture_client_v1 {
  std::mutex mutex;
  std::map<std::uint64_t, bool> canceled;
  std::vector<std::thread> workers;
  std::uint64_t next_request_id{1};
};

namespace {

std::atomic<unsigned> completion_frees{0};
std::atomic<unsigned> cancel_calls{0};
constexpr std::uint8_t png_bytes[] = {137, 80, 78, 71, 13, 10, 26, 10};
constexpr char sdk_semver[] = "0.1.0";

void require(bool condition) {
  if (!condition) {
    throw std::runtime_error("C++ wrapper contract assertion failed");
  }
}

snaploom_capture_completion_v1* completed(std::uint64_t request_id,
                                          bool malformed) {
  auto* png = new std::uint8_t[sizeof(png_bytes)];
  std::memcpy(png, png_bytes, sizeof(png_bytes));
  auto* completion = new snaploom_capture_completion_v1{};
  completion->struct_size = sizeof(*completion);
  completion->kind = SNAPLOOM_COMPLETION_COMPLETED;
  completion->flags = SNAPLOOM_COMPLETION_FLAG_CLIPBOARD_WRITTEN;
  completion->request_id = request_id;
  completion->png_data = png;
  completion->png_size = malformed ? 0 : sizeof(png_bytes);
  completion->pixel_width = 1;
  completion->pixel_height = 1;
  return completion;
}

snaploom_capture_completion_v1* canceled(std::uint64_t request_id) {
  auto* completion = new snaploom_capture_completion_v1{};
  completion->struct_size = sizeof(*completion);
  completion->kind = SNAPLOOM_COMPLETION_CANCELED;
  completion->request_id = request_id;
  return completion;
}

}  // namespace

extern "C" {

snaploom_status_v1 SNAPLOOM_CALL snaploom_capture_version_v1(
    snaploom_capture_version_info_v1* out_version) {
  if (out_version == nullptr || out_version->struct_size < sizeof(*out_version)) {
    return SNAPLOOM_STATUS_INVALID_STRUCT_SIZE;
  }
  out_version->abi_major = 1;
  out_version->sdk_semver.data =
      reinterpret_cast<const std::uint8_t*>(sdk_semver);
  out_version->sdk_semver.length = sizeof(sdk_semver) - 1;
  return SNAPLOOM_STATUS_OK;
}

snaploom_status_v1 SNAPLOOM_CALL snaploom_capture_client_create_v1(
    const snaploom_capture_client_config_v1*,
    snaploom_capture_client_v1** out_client) {
  if (out_client == nullptr) {
    return SNAPLOOM_STATUS_INVALID_ARGUMENT;
  }
  *out_client = new snaploom_capture_client_v1{};
  return SNAPLOOM_STATUS_OK;
}

snaploom_status_v1 SNAPLOOM_CALL snaploom_capture_start_v1(
    snaploom_capture_client_v1* client,
    const snaploom_capture_options_v1* options,
    snaploom_capture_callback_v1 callback, void* user_data,
    snaploom_request_id_v1* out_request_id) {
  if (client == nullptr || callback == nullptr) {
    return SNAPLOOM_STATUS_INVALID_ARGUMENT;
  }
  std::lock_guard<std::mutex> lock(client->mutex);
  const auto request_id = client->next_request_id++;
  client->canceled.emplace(request_id, false);
  if (out_request_id != nullptr) {
    *out_request_id = request_id;
  }
  const bool malformed =
      options != nullptr && options->interaction_timeout_ms == 7;
  client->workers.emplace_back([=] {
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
    bool was_canceled = false;
    {
      std::lock_guard<std::mutex> request_lock(client->mutex);
      was_canceled = client->canceled[request_id];
    }
    callback(client, was_canceled ? canceled(request_id)
                                  : completed(request_id, malformed),
             user_data);
  });
  return SNAPLOOM_STATUS_OK;
}

snaploom_status_v1 SNAPLOOM_CALL snaploom_capture_cancel_v1(
    snaploom_capture_client_v1* client, snaploom_request_id_v1 request_id) {
  if (client == nullptr) {
    return SNAPLOOM_STATUS_INVALID_ARGUMENT;
  }
  std::lock_guard<std::mutex> lock(client->mutex);
  const auto request = client->canceled.find(request_id);
  if (request == client->canceled.end()) {
    return SNAPLOOM_STATUS_NOT_FOUND;
  }
  request->second = true;
  ++cancel_calls;
  return SNAPLOOM_STATUS_OK;
}

snaploom_status_v1 SNAPLOOM_CALL snaploom_capture_client_destroy_v1(
    snaploom_capture_client_v1* client) {
  if (client == nullptr) {
    return SNAPLOOM_STATUS_INVALID_ARGUMENT;
  }
  for (auto& worker : client->workers) {
    worker.join();
  }
  delete client;
  return SNAPLOOM_STATUS_OK;
}

void SNAPLOOM_CALL snaploom_capture_completion_free_v1(
    snaploom_capture_completion_v1* completion) {
  if (completion == nullptr) {
    return;
  }
  delete[] completion->png_data;
  delete completion;
  ++completion_frees;
}

const char* SNAPLOOM_CALL snaploom_capture_error_name_v1(
    snaploom_error_v1 error_code) {
  return error_code == SNAPLOOM_ERROR_BUSY ? "BUSY" : "UNKNOWN";
}

}  // extern "C"

int main() {
  static_assert(!std::is_copy_constructible_v<snaploom::CaptureClient>);
  static_assert(std::is_move_constructible_v<snaploom::CaptureClient>);
  static_assert(!std::is_copy_constructible_v<snaploom::CaptureOperation>);
  static_assert(std::is_move_constructible_v<snaploom::CaptureOperation>);

  {
    snaploom::CaptureClient client;
    auto future = client.capture();
    auto outcome = future.get();
    const auto& result = std::get<snaploom::CaptureResult>(outcome);
    require(result.png.size() == sizeof(png_bytes));
    require(result.pixel_width == 1 && result.pixel_height == 1);
    require(result.clipboard_written);
  }

  {
    snaploom::CaptureClient client;
    auto operation = client.start();
    auto future = operation.take_future();
    bool second_take_failed = false;
    try {
      static_cast<void>(operation.take_future());
    } catch (const std::logic_error&) {
      second_take_failed = true;
    }
    require(second_take_failed);
    auto moved = std::move(operation);
    require(!moved.request_cancel());
    require(std::holds_alternative<snaploom::CaptureCanceled>(future.get()));
  }

  {
    snaploom::CaptureClient client;
    auto operation = client.start();
    auto future = operation.take_future();
    const auto cancels_before = cancel_calls.load();
    { auto dropped = std::move(operation); }
    require(cancel_calls.load() == cancels_before);
    require(std::holds_alternative<snaploom::CaptureResult>(future.get()));
  }

  {
    std::future<snaploom::CaptureOutcome> future;
    {
      snaploom::CaptureClient client;
      future = client.capture();
    }
    require(std::holds_alternative<snaploom::CaptureResult>(future.get()));
  }

  {
    snaploom::CaptureClient client;
    auto future = client.capture(snaploom::CaptureOptions{false, 7});
    bool invalid_completion_failed = false;
    try {
      static_cast<void>(future.get());
    } catch (const std::runtime_error&) {
      invalid_completion_failed = true;
    }
    require(invalid_completion_failed);
  }

  require(completion_frees.load() == 5);
  return 0;
}
