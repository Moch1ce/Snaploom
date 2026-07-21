#ifndef SNAPLOOM_CAPTURE_HPP
#define SNAPLOOM_CAPTURE_HPP

#include <snaploom/snaploom_capture.h>

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <exception>
#include <future>
#include <limits>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <system_error>
#include <utility>
#include <variant>
#include <vector>

namespace snaploom {

constexpr std::uint32_t abi_major_v1 = 1;
constexpr std::uint32_t sdk_semver_major_v1 = 0;
constexpr const char* sdk_package_version_v1 = "0.1.0";
constexpr std::uint64_t maximum_png_bytes_v1 = 128ULL * 1024ULL * 1024ULL;

struct Error {
  std::uint32_t value{SNAPLOOM_ERROR_NONE};

  [[nodiscard]] const char* name() const noexcept {
    return snaploom_capture_error_name_v1(value);
  }
};

struct ClientOptions {
  std::string host_executable_override;
  std::uint32_t launch_timeout_ms{0};
  std::uint32_t handshake_timeout_ms{0};
};

struct CaptureOptions {
  bool disable_clipboard{false};
  std::uint64_t interaction_timeout_ms{0};
};

struct RuntimeVersion {
  std::uint32_t abi_major{};
  std::string sdk_semver;
};

struct CaptureResult {
  std::vector<std::byte> png;
  std::uint32_t pixel_width{};
  std::uint32_t pixel_height{};
  bool clipboard_written{};
};

struct CaptureCanceled {};

struct CaptureFailure {
  Error error;
  bool retryable{};
};

using CaptureOutcome =
    std::variant<CaptureResult, CaptureCanceled, CaptureFailure>;

class CaptureSdkError : public std::system_error {
 public:
  explicit CaptureSdkError(std::uint32_t status)
      : std::system_error(static_cast<int>(status), category()) {}

  [[nodiscard]] std::uint32_t native_status() const noexcept {
    return static_cast<std::uint32_t>(code().value());
  }

 private:
  class StatusCategory final : public std::error_category {
   public:
    [[nodiscard]] const char* name() const noexcept override {
      return "snaploom.capture";
    }

    [[nodiscard]] std::string message(int value) const override {
      switch (static_cast<std::uint32_t>(value)) {
        case SNAPLOOM_STATUS_OK:
          return "OK";
        case SNAPLOOM_STATUS_INVALID_ARGUMENT:
          return "INVALID_ARGUMENT";
        case SNAPLOOM_STATUS_INVALID_STRUCT_SIZE:
          return "INVALID_STRUCT_SIZE";
        case SNAPLOOM_STATUS_CLIENT_CLOSED:
          return "CLIENT_CLOSED";
        case SNAPLOOM_STATUS_CALLBACK_CONTEXT:
          return "CALLBACK_CONTEXT";
        case SNAPLOOM_STATUS_NOT_FOUND:
          return "NOT_FOUND";
        case SNAPLOOM_STATUS_OUT_OF_MEMORY:
          return "OUT_OF_MEMORY";
        default:
          return "INTERNAL";
      }
    }
  };

  [[nodiscard]] static const std::error_category& category() noexcept {
    static const StatusCategory instance;
    return instance;
  }
};

class CaptureStartError final : public CaptureSdkError {
 public:
  explicit CaptureStartError(std::uint32_t status) : CaptureSdkError(status) {}
};

namespace detail {

inline std::error_code status_error(std::uint32_t status) noexcept {
  try {
    throw CaptureSdkError(status);
  } catch (const CaptureSdkError& error) {
    return error.code();
  } catch (...) {
    return std::make_error_code(std::errc::io_error);
  }
}

inline void throw_status(std::uint32_t status) {
  if (status == SNAPLOOM_STATUS_OUT_OF_MEMORY) {
    throw std::bad_alloc{};
  }
  throw CaptureSdkError(status);
}

inline void throw_start_status(std::uint32_t status) {
  if (status == SNAPLOOM_STATUS_OUT_OF_MEMORY) {
    throw std::bad_alloc{};
  }
  throw CaptureStartError(status);
}

struct ClientState {
  std::mutex mutex;
  snaploom_capture_client_v1* native{};
};

struct OperationState {
  std::mutex mutex;
  std::promise<CaptureOutcome> promise;
  bool future_taken{false};
  std::shared_ptr<ClientState> client;
  snaploom_request_id_v1 request_id{};

  std::future<CaptureOutcome> take_future() {
    std::lock_guard<std::mutex> lock(mutex);
    if (future_taken) {
      throw std::logic_error("Snaploom capture future was already taken");
    }
    future_taken = true;
    return promise.get_future();
  }

  void set_exception(std::exception_ptr exception) noexcept {
    try {
      promise.set_exception(std::move(exception));
    } catch (...) {
    }
  }
};

class CompletionGuard final {
 public:
  explicit CompletionGuard(snaploom_capture_completion_v1* completion) noexcept
      : completion_(completion) {}
  CompletionGuard(const CompletionGuard&) = delete;
  CompletionGuard& operator=(const CompletionGuard&) = delete;
  ~CompletionGuard() { snaploom_capture_completion_free_v1(completion_); }

 private:
  snaploom_capture_completion_v1* completion_;
};

inline CaptureOutcome copy_outcome(
    const snaploom_capture_completion_v1& completion) {
  if (completion.struct_size < sizeof(snaploom_capture_completion_v1) ||
      (completion.flags & ~(SNAPLOOM_COMPLETION_FLAG_CLIPBOARD_WRITTEN |
                            SNAPLOOM_COMPLETION_FLAG_RETRYABLE)) != 0) {
    throw std::runtime_error("invalid Snaploom completion layout");
  }

  if (completion.kind == SNAPLOOM_COMPLETION_COMPLETED) {
    if (completion.error_code != SNAPLOOM_ERROR_NONE ||
        completion.png_data == nullptr || completion.png_size == 0 ||
        completion.png_size > maximum_png_bytes_v1 ||
        completion.png_size >
            static_cast<std::uint64_t>(std::numeric_limits<std::size_t>::max()) ||
        completion.pixel_width == 0 || completion.pixel_height == 0 ||
        (completion.flags & SNAPLOOM_COMPLETION_FLAG_RETRYABLE) != 0) {
      throw std::runtime_error("invalid completed Snaploom capture");
    }
    CaptureResult result;
    result.png.resize(static_cast<std::size_t>(completion.png_size));
    std::memcpy(result.png.data(), completion.png_data, result.png.size());
    result.pixel_width = completion.pixel_width;
    result.pixel_height = completion.pixel_height;
    result.clipboard_written =
        (completion.flags & SNAPLOOM_COMPLETION_FLAG_CLIPBOARD_WRITTEN) != 0;
    return result;
  }

  const bool has_image = completion.png_data != nullptr ||
                         completion.png_size != 0 ||
                         completion.pixel_width != 0 ||
                         completion.pixel_height != 0;
  if (has_image ||
      (completion.flags & SNAPLOOM_COMPLETION_FLAG_CLIPBOARD_WRITTEN) != 0) {
    throw std::runtime_error("invalid terminal Snaploom capture");
  }
  if (completion.kind == SNAPLOOM_COMPLETION_CANCELED) {
    if (completion.error_code != SNAPLOOM_ERROR_NONE || completion.flags != 0) {
      throw std::runtime_error("invalid canceled Snaploom capture");
    }
    return CaptureCanceled{};
  }
  if (completion.kind == SNAPLOOM_COMPLETION_FAILED) {
    if (completion.error_code == SNAPLOOM_ERROR_NONE) {
      throw std::runtime_error("invalid failed Snaploom capture");
    }
    return CaptureFailure{
        Error{completion.error_code},
        (completion.flags & SNAPLOOM_COMPLETION_FLAG_RETRYABLE) != 0};
  }
  throw std::runtime_error("unknown Snaploom completion kind");
}

inline void SNAPLOOM_CALL callback_thunk(
    snaploom_capture_client_v1*, snaploom_capture_completion_v1* completion,
    void* user_data) noexcept {
  CompletionGuard completion_guard(completion);
  std::unique_ptr<std::shared_ptr<OperationState>> holder(
      static_cast<std::shared_ptr<OperationState>*>(user_data));
  if (!holder || !*holder) {
    return;
  }
  try {
    if (completion == nullptr) {
      throw std::runtime_error("null Snaploom completion");
    }
    (*holder)->promise.set_value(copy_outcome(*completion));
  } catch (...) {
    (*holder)->set_exception(std::current_exception());
  }
}

inline RuntimeVersion query_runtime_version() {
  snaploom_capture_version_info_v1 version{};
  version.struct_size = sizeof(version);
  const auto status = snaploom_capture_version_v1(&version);
  if (status != SNAPLOOM_STATUS_OK) {
    throw_status(status);
  }
  if (version.abi_major != abi_major_v1 || version.sdk_semver.data == nullptr ||
      version.sdk_semver.length == 0 ||
      version.sdk_semver.length >
          static_cast<std::uint64_t>(std::numeric_limits<std::size_t>::max())) {
    throw std::runtime_error("incompatible Snaploom Capture ABI");
  }
  std::string sdk_semver(
      reinterpret_cast<const char*>(version.sdk_semver.data),
      static_cast<std::size_t>(version.sdk_semver.length));
  std::uint32_t semver_major = 0;
  std::size_t cursor = 0;
  while (cursor < sdk_semver.size() && sdk_semver[cursor] >= '0' &&
         sdk_semver[cursor] <= '9') {
    const auto digit = static_cast<std::uint32_t>(sdk_semver[cursor] - '0');
    if (semver_major >
        (std::numeric_limits<std::uint32_t>::max() - digit) / 10) {
      throw std::runtime_error("invalid Snaploom Capture SDK semver");
    }
    semver_major = semver_major * 10 + digit;
    ++cursor;
  }
  if (cursor == 0 || cursor >= sdk_semver.size() || sdk_semver[cursor] != '.' ||
      semver_major != sdk_semver_major_v1) {
    throw std::runtime_error("incompatible Snaploom Capture SDK semver");
  }
  return RuntimeVersion{version.abi_major, std::move(sdk_semver)};
}

}  // namespace detail

class CaptureOperation final {
 public:
  CaptureOperation(CaptureOperation&&) noexcept = default;
  CaptureOperation& operator=(CaptureOperation&&) noexcept = default;
  CaptureOperation(const CaptureOperation&) = delete;
  CaptureOperation& operator=(const CaptureOperation&) = delete;
  ~CaptureOperation() = default;

  [[nodiscard]] std::future<CaptureOutcome> take_future() {
    if (!state_) {
      throw std::logic_error("moved-from Snaploom capture operation");
    }
    return state_->take_future();
  }

  [[nodiscard]] std::error_code request_cancel() noexcept {
    if (!state_ || !state_->client) {
      return detail::status_error(SNAPLOOM_STATUS_CLIENT_CLOSED);
    }
    std::lock_guard<std::mutex> lock(state_->client->mutex);
    if (state_->client->native == nullptr) {
      return detail::status_error(SNAPLOOM_STATUS_CLIENT_CLOSED);
    }
    const auto status = snaploom_capture_cancel_v1(state_->client->native,
                                                    state_->request_id);
    return status == SNAPLOOM_STATUS_OK ? std::error_code{}
                                        : detail::status_error(status);
  }

 private:
  friend class CaptureClient;
  explicit CaptureOperation(std::shared_ptr<detail::OperationState> state)
      : state_(std::move(state)) {}

  std::shared_ptr<detail::OperationState> state_;
};

class CaptureClient final {
 public:
  explicit CaptureClient(ClientOptions options = {})
      : runtime_version_(detail::query_runtime_version()),
        state_(std::make_shared<detail::ClientState>()) {
    snaploom_capture_client_config_v1 config =
        SNAPLOOM_CAPTURE_CLIENT_CONFIG_V1_INIT;
    config.launch_timeout_ms = options.launch_timeout_ms;
    config.handshake_timeout_ms = options.handshake_timeout_ms;
    if (!options.host_executable_override.empty()) {
      config.host_executable_override.data = reinterpret_cast<const std::uint8_t*>(
          options.host_executable_override.data());
      config.host_executable_override.length =
          static_cast<std::uint64_t>(options.host_executable_override.size());
    }
    const auto status =
        snaploom_capture_client_create_v1(&config, &state_->native);
    if (status != SNAPLOOM_STATUS_OK) {
      detail::throw_status(status);
    }
  }

  CaptureClient(CaptureClient&& other) noexcept
      : runtime_version_(std::move(other.runtime_version_)),
        state_(std::move(other.state_)) {}

  CaptureClient& operator=(CaptureClient&& other) noexcept {
    if (this != &other) {
      close_noexcept();
      runtime_version_ = std::move(other.runtime_version_);
      state_ = std::move(other.state_);
    }
    return *this;
  }

  CaptureClient(const CaptureClient&) = delete;
  CaptureClient& operator=(const CaptureClient&) = delete;

  ~CaptureClient() { close_noexcept(); }

  [[nodiscard]] const RuntimeVersion& runtime_version() const noexcept {
    return runtime_version_;
  }

  [[nodiscard]] CaptureOperation start(CaptureOptions options = {}) {
    return CaptureOperation(start_impl(options, true));
  }

  [[nodiscard]] std::future<CaptureOutcome> capture(
      CaptureOptions options = {}) {
    auto operation = start_impl(options, false);
    return operation->take_future();
  }

  void close() {
    auto* native = take_native();
    if (native == nullptr) {
      return;
    }
    const auto status = snaploom_capture_client_destroy_v1(native);
    if (status != SNAPLOOM_STATUS_OK) {
      std::lock_guard<std::mutex> lock(state_->mutex);
      if (state_->native == nullptr) {
        state_->native = native;
      }
      detail::throw_status(status);
    }
  }

 private:
  [[nodiscard]] snaploom_capture_client_v1* take_native() noexcept {
    if (!state_) {
      return nullptr;
    }
    std::lock_guard<std::mutex> lock(state_->mutex);
    return std::exchange(state_->native, nullptr);
  }

  void close_noexcept() noexcept {
    try {
      close();
    } catch (...) {
    }
  }

  [[nodiscard]] std::shared_ptr<detail::OperationState> start_impl(
      CaptureOptions options, bool cancellable) {
    if (!state_) {
      detail::throw_status(SNAPLOOM_STATUS_CLIENT_CLOSED);
    }
    auto operation = std::make_shared<detail::OperationState>();
    operation->client = state_;
    auto holder =
        std::make_unique<std::shared_ptr<detail::OperationState>>(operation);
    snaploom_capture_options_v1 native_options = SNAPLOOM_CAPTURE_OPTIONS_V1_INIT;
    native_options.flags = options.disable_clipboard
                               ? SNAPLOOM_CAPTURE_FLAG_DISABLE_CLIPBOARD
                               : 0;
    native_options.interaction_timeout_ms = options.interaction_timeout_ms;

    std::lock_guard<std::mutex> lock(state_->mutex);
    if (state_->native == nullptr) {
      detail::throw_status(SNAPLOOM_STATUS_CLIENT_CLOSED);
    }
    snaploom_request_id_v1 request_id{};
    const auto status = snaploom_capture_start_v1(
        state_->native, &native_options, detail::callback_thunk, holder.get(),
        cancellable ? &request_id : nullptr);
    if (status != SNAPLOOM_STATUS_OK) {
      detail::throw_start_status(status);
    }
    holder.release();
    operation->request_id = request_id;
    return operation;
  }

  RuntimeVersion runtime_version_;
  std::shared_ptr<detail::ClientState> state_;
};

}  // namespace snaploom

#endif
