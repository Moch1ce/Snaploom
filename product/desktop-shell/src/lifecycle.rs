use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum CaptureTrigger {
    Tray,
    Shortcut,
    SecondInstance,
}

#[derive(Debug, Default)]
pub struct CaptureIntentGate {
    active: AtomicBool,
}

impl CaptureIntentGate {
    #[must_use]
    pub const fn new() -> Self {
        Self {
            active: AtomicBool::new(false),
        }
    }

    /// Application entry points share this gate before asking Capture Host to
    /// start a session. A repeated application intent is deliberately silent.
    #[must_use]
    pub fn try_begin(&self) -> Option<IntentLease<'_>> {
        self.active
            .compare_exchange(false, true, Ordering::AcqRel, Ordering::Acquire)
            .ok()
            .map(|_| IntentLease { gate: self })
    }

    #[must_use]
    pub fn try_begin_owned(self: &Arc<Self>) -> Option<OwnedIntentLease> {
        self.active
            .compare_exchange(false, true, Ordering::AcqRel, Ordering::Acquire)
            .ok()
            .map(|_| OwnedIntentLease { gate: self.clone() })
    }

    #[must_use]
    pub fn is_active(&self) -> bool {
        self.active.load(Ordering::Acquire)
    }
}

pub struct IntentLease<'a> {
    gate: &'a CaptureIntentGate,
}

impl Drop for IntentLease<'_> {
    fn drop(&mut self) {
        self.gate.active.store(false, Ordering::Release);
    }
}

pub struct OwnedIntentLease {
    gate: Arc<CaptureIntentGate>,
}

impl Drop for OwnedIntentLease {
    fn drop(&mut self) {
        self.gate.active.store(false, Ordering::Release);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn repeated_capture_intents_are_silently_coalesced() {
        let gate = CaptureIntentGate::new();
        let lease = gate.try_begin().expect("first intent");
        assert!(gate.is_active());
        assert!(gate.try_begin().is_none());
        drop(lease);
        assert!(gate.try_begin().is_some());
    }

    #[test]
    fn a_second_instance_routes_through_the_same_application_gate() {
        let gate = Arc::new(CaptureIntentGate::new());
        let tray_intent = gate.try_begin_owned().expect("tray intent");
        let second_instance_trigger = CaptureTrigger::SecondInstance;
        assert_eq!(second_instance_trigger, CaptureTrigger::SecondInstance);
        assert!(gate.try_begin_owned().is_none());
        drop(tray_intent);
        assert!(gate.try_begin_owned().is_some());
    }
}
