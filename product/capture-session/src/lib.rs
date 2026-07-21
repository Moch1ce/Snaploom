use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;
use zeroize::Zeroize;

pub const HOST_IDLE_GRACE: Duration = Duration::from_secs(30);

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct ClientId([u8; 16]);

impl ClientId {
    #[must_use]
    pub const fn from_u128(value: u128) -> Self {
        Self(value.to_le_bytes())
    }

    #[must_use]
    pub const fn from_bytes(value: [u8; 16]) -> Self {
        Self(value)
    }

    #[must_use]
    pub const fn as_bytes(self) -> [u8; 16] {
        self.0
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SessionOrigin {
    App,
    Sdk,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SessionPhase {
    Preparing,
    Interactive,
    Finalizing,
    CleaningUp,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BeginError {
    Busy,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum GateSnapshot {
    Idle,
    Active {
        session_id: u64,
        owner: ClientId,
        origin: SessionOrigin,
        phase: SessionPhase,
        canceled: bool,
    },
}

enum GateState {
    Idle,
    Active {
        session_id: u64,
        owner: ClientId,
        origin: SessionOrigin,
        phase: SessionPhase,
        cancel: Arc<AtomicBool>,
    },
}

struct GateInner {
    state: Mutex<GateState>,
    next_session_id: AtomicU64,
}

#[derive(Clone)]
pub struct CaptureSessionGate {
    inner: Arc<GateInner>,
}

impl Default for CaptureSessionGate {
    fn default() -> Self {
        Self {
            inner: Arc::new(GateInner {
                state: Mutex::new(GateState::Idle),
                next_session_id: AtomicU64::new(1),
            }),
        }
    }
}

impl CaptureSessionGate {
    pub fn try_begin(
        &self,
        owner: ClientId,
        origin: SessionOrigin,
    ) -> Result<SessionLease, BeginError> {
        let mut state = self
            .inner
            .state
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if matches!(*state, GateState::Active { .. }) {
            return Err(BeginError::Busy);
        }
        let session_id = self.next_nonzero_session_id();
        let cancel = Arc::new(AtomicBool::new(false));
        *state = GateState::Active {
            session_id,
            owner,
            origin,
            phase: SessionPhase::Preparing,
            cancel: cancel.clone(),
        };
        Ok(SessionLease {
            inner: self.inner.clone(),
            session_id,
            cancel,
            released: false,
        })
    }

    #[must_use]
    pub fn disconnect(&self, owner: ClientId) -> bool {
        let state = self
            .inner
            .state
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        let GateState::Active {
            owner: active_owner,
            cancel,
            ..
        } = &*state
        else {
            return false;
        };
        if *active_owner != owner {
            return false;
        }
        cancel.store(true, Ordering::Release);
        true
    }

    #[must_use]
    pub fn snapshot(&self) -> GateSnapshot {
        let state = self
            .inner
            .state
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        match &*state {
            GateState::Idle => GateSnapshot::Idle,
            GateState::Active {
                session_id,
                owner,
                origin,
                phase,
                cancel,
            } => GateSnapshot::Active {
                session_id: *session_id,
                owner: *owner,
                origin: *origin,
                phase: *phase,
                canceled: cancel.load(Ordering::Acquire),
            },
        }
    }

    fn next_nonzero_session_id(&self) -> u64 {
        loop {
            let session_id = self.inner.next_session_id.fetch_add(1, Ordering::Relaxed);
            if session_id != 0 {
                return session_id;
            }
        }
    }
}

pub struct SessionLease {
    inner: Arc<GateInner>,
    session_id: u64,
    cancel: Arc<AtomicBool>,
    released: bool,
}

impl SessionLease {
    #[must_use]
    pub const fn id(&self) -> u64 {
        self.session_id
    }

    #[must_use]
    pub fn is_canceled(&self) -> bool {
        self.cancel.load(Ordering::Acquire)
    }

    pub fn cancel(&self) {
        self.cancel.store(true, Ordering::Release);
    }

    #[must_use]
    pub fn control(&self) -> SessionControl {
        SessionControl {
            cancel: self.cancel.clone(),
        }
    }

    pub fn set_phase(&mut self, phase: SessionPhase) {
        let mut state = self
            .inner
            .state
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if let GateState::Active {
            session_id,
            phase: active_phase,
            ..
        } = &mut *state
            && *session_id == self.session_id
        {
            *active_phase = phase;
        }
    }

    pub fn finish_cleanup(mut self) {
        self.release();
    }

    fn release(&mut self) {
        if self.released {
            return;
        }
        let mut state = self
            .inner
            .state
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if matches!(
            &*state,
            GateState::Active { session_id, .. } if *session_id == self.session_id
        ) {
            *state = GateState::Idle;
        }
        self.released = true;
    }
}

#[derive(Clone)]
pub struct SessionControl {
    cancel: Arc<AtomicBool>,
}

impl SessionControl {
    #[must_use]
    pub fn is_canceled(&self) -> bool {
        self.cancel.load(Ordering::Acquire)
    }

    pub fn cancel(&self) {
        self.cancel.store(true, Ordering::Release);
    }
}

#[derive(Debug, Clone)]
pub struct CaptureRequest {
    pub origin: SessionOrigin,
    pub clipboard_enabled: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SessionFailure {
    PlatformUnavailable,
    DisplayUnavailable,
    CaptureUnavailable,
    CaptureTimeout,
    PixelConversionFailed,
    Internal,
}

#[derive(Debug)]
pub enum SessionTerminal {
    Completed {
        png: SensitivePng,
        pixel_width: u32,
        pixel_height: u32,
        clipboard_written: bool,
    },
    Canceled {
        by_user: bool,
    },
    Failed {
        failure: SessionFailure,
        retryable: bool,
    },
}

pub struct SensitivePng(Vec<u8>);

impl SensitivePng {
    #[must_use]
    pub fn new(bytes: Vec<u8>) -> Self {
        Self(bytes)
    }

    #[must_use]
    pub fn as_slice(&self) -> &[u8] {
        &self.0
    }
}

impl std::fmt::Debug for SensitivePng {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("SensitivePng")
            .field("byte_length", &self.0.len())
            .finish()
    }
}

impl From<Vec<u8>> for SensitivePng {
    fn from(bytes: Vec<u8>) -> Self {
        Self::new(bytes)
    }
}

impl Drop for SensitivePng {
    fn drop(&mut self) {
        self.0.zeroize();
    }
}

pub trait SessionBackend: Send + Sync + 'static {
    fn run(&self, request: CaptureRequest, control: SessionControl) -> SessionTerminal;
}

#[derive(Debug, Default)]
pub struct UnavailableSessionBackend;

impl SessionBackend for UnavailableSessionBackend {
    fn run(&self, _request: CaptureRequest, _control: SessionControl) -> SessionTerminal {
        SessionTerminal::Failed {
            failure: SessionFailure::PlatformUnavailable,
            retryable: false,
        }
    }
}

impl std::fmt::Debug for SessionLease {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("SessionLease")
            .field("session_id", &self.session_id)
            .field("canceled", &self.is_canceled())
            .finish_non_exhaustive()
    }
}

impl Drop for SessionLease {
    fn drop(&mut self) {
        self.release();
    }
}

#[derive(Debug)]
pub struct HostLifecycle {
    clients: usize,
    app_keepalives: usize,
    sessions: usize,
    idle_since: Option<Duration>,
}

impl Default for HostLifecycle {
    fn default() -> Self {
        Self {
            clients: 0,
            app_keepalives: 0,
            sessions: 0,
            idle_since: Some(Duration::ZERO),
        }
    }
}

impl HostLifecycle {
    pub fn client_connected(&mut self, _now: Duration) {
        self.clients = self.clients.saturating_add(1);
        self.idle_since = None;
    }

    pub fn client_disconnected(&mut self, now: Duration) {
        self.clients = self.clients.saturating_sub(1);
        self.refresh_idle(now);
    }

    pub fn app_keepalive_started(&mut self, _now: Duration) {
        self.app_keepalives = self.app_keepalives.saturating_add(1);
        self.idle_since = None;
    }

    pub fn app_keepalive_finished(&mut self, now: Duration) {
        self.app_keepalives = self.app_keepalives.saturating_sub(1);
        self.refresh_idle(now);
    }

    pub fn session_started(&mut self, _now: Duration) {
        self.sessions = self.sessions.saturating_add(1);
        self.idle_since = None;
    }

    pub fn session_finished(&mut self, now: Duration) {
        self.sessions = self.sessions.saturating_sub(1);
        self.refresh_idle(now);
    }

    #[must_use]
    pub fn should_exit(&self, now: Duration) -> bool {
        self.idle_since
            .is_some_and(|idle_since| now.saturating_sub(idle_since) >= HOST_IDLE_GRACE)
    }

    fn refresh_idle(&mut self, now: Duration) {
        if self.clients == 0 && self.app_keepalives == 0 && self.sessions == 0 {
            self.idle_since.get_or_insert(now);
        } else {
            self.idle_since = None;
        }
    }
}

#[cfg(test)]
mod tests {
    use std::sync::{Arc, Barrier};
    use std::thread;
    use std::time::Duration;

    use super::{
        BeginError, CaptureSessionGate, ClientId, HostLifecycle, SessionOrigin, SessionPhase,
    };

    #[test]
    fn concurrent_app_and_sdk_requests_have_one_global_winner() {
        let gate = Arc::new(CaptureSessionGate::default());
        let barrier = Arc::new(Barrier::new(21));
        let mut workers = Vec::new();
        for index in 0..20_u128 {
            let gate = gate.clone();
            let barrier = barrier.clone();
            workers.push(thread::spawn(move || {
                barrier.wait();
                gate.try_begin(
                    ClientId::from_u128(index + 1),
                    if index % 2 == 0 {
                        SessionOrigin::App
                    } else {
                        SessionOrigin::Sdk
                    },
                )
            }));
        }
        barrier.wait();
        let results = workers
            .into_iter()
            .map(|worker| worker.join().unwrap())
            .collect::<Vec<_>>();
        assert_eq!(results.iter().filter(|result| result.is_ok()).count(), 1);
        assert_eq!(
            results
                .iter()
                .filter(|result| matches!(result, Err(BeginError::Busy)))
                .count(),
            19
        );
    }

    #[test]
    fn disconnect_only_cancels_the_owning_session_and_cleanup_releases_gate() {
        let gate = CaptureSessionGate::default();
        let owner = ClientId::from_u128(1);
        let unrelated = ClientId::from_u128(2);
        let mut lease = gate.try_begin(owner, SessionOrigin::Sdk).unwrap();
        lease.set_phase(SessionPhase::Interactive);
        assert!(!gate.disconnect(unrelated));
        assert!(!lease.is_canceled());
        assert!(gate.disconnect(owner));
        assert!(lease.is_canceled());
        assert!(matches!(
            gate.try_begin(unrelated, SessionOrigin::Sdk),
            Err(BeginError::Busy)
        ));
        lease.finish_cleanup();
        assert!(gate.try_begin(unrelated, SessionOrigin::Sdk).is_ok());
    }

    #[test]
    fn idle_grace_requires_no_clients_keepalive_or_session_for_thirty_seconds() {
        let mut lifecycle = HostLifecycle::default();
        lifecycle.client_connected(Duration::ZERO);
        lifecycle.client_disconnected(Duration::from_secs(5));
        assert!(!lifecycle.should_exit(Duration::from_secs(34)));
        assert!(lifecycle.should_exit(Duration::from_secs(35)));

        lifecycle.client_connected(Duration::from_secs(36));
        assert!(!lifecycle.should_exit(Duration::from_secs(100)));
        lifecycle.client_disconnected(Duration::from_secs(101));
        lifecycle.session_started(Duration::from_secs(102));
        lifecycle.session_finished(Duration::from_secs(140));
        assert!(!lifecycle.should_exit(Duration::from_secs(169)));
        assert!(lifecycle.should_exit(Duration::from_secs(170)));
    }
}
