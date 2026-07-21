use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct PhysicalSize {
    pub width: u32,
    pub height: u32,
}

pub trait PlatformAdapter: Send + Sync {
    fn platform_name(&self) -> &'static str;
}
