use snaploom_platform_contract::PlatformAdapter;

#[derive(Debug, Default)]
pub struct FakePlatform;

impl PlatformAdapter for FakePlatform {
    fn platform_name(&self) -> &'static str {
        "fake"
    }
}
