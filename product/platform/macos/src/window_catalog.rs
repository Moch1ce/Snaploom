use std::collections::{HashMap, HashSet};
use std::fmt::Write as _;

use snaploom_platform_contract::WindowCandidate;

use crate::coordinates::{DisplayGeometry, PointRect};

#[derive(Debug, Clone, PartialEq)]
pub(crate) struct SckWindowRecord {
    pub(crate) window_id: u32,
    pub(crate) owner_pid: i32,
    pub(crate) owner_bundle: Option<String>,
    pub(crate) layer: i64,
    pub(crate) on_screen: bool,
    pub(crate) frame: PointRect,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub(crate) struct CgWindowRecord {
    pub(crate) window_id: u32,
    pub(crate) owner_pid: i32,
    pub(crate) layer: i32,
    pub(crate) alpha: f64,
    pub(crate) on_screen: bool,
}

const SYSTEM_UI_BUNDLES: &[&str] = &[
    "com.apple.controlcenter",
    "com.apple.dock",
    "com.apple.notificationcenterui",
    "com.apple.systemuiserver",
    "com.apple.windowmanager",
];

pub(crate) fn merge_windows(
    sck: Vec<SckWindowRecord>,
    cg_front_to_back: &[CgWindowRecord],
    self_pid: i32,
    geometry: DisplayGeometry,
    secret: &[u8; 16],
) -> Vec<WindowCandidate> {
    let by_id: HashMap<_, _> = sck
        .into_iter()
        .map(|window| (window.window_id, window))
        .collect();
    let mut emitted = HashSet::new();
    cg_front_to_back
        .iter()
        .enumerate()
        .filter_map(|(z_order, cg)| {
            let window = by_id.get(&cg.window_id)?;
            if !emitted.insert(cg.window_id)
                || window.owner_pid == self_pid
                || cg.owner_pid == self_pid
                || window.owner_pid != cg.owner_pid
                || window.layer != 0
                || cg.layer != 0
                || !window.on_screen
                || !cg.on_screen
                || !cg.alpha.is_finite()
                || cg.alpha <= 0.01
                || window
                    .owner_bundle
                    .as_deref()
                    .is_some_and(|bundle| SYSTEM_UI_BUNDLES.contains(&bundle))
            {
                return None;
            }
            let bounds = geometry.window_local(window.frame)?;
            Some(WindowCandidate {
                stable_id: stable_id(cg.window_id, secret),
                z_order: u32::try_from(z_order).unwrap_or(u32::MAX),
                bounds,
            })
        })
        .collect()
}

fn stable_id(window_id: u32, secret: &[u8; 16]) -> String {
    let mut hash = 0xcbf2_9ce4_8422_2325_u64;
    for byte in secret.iter().copied().chain(window_id.to_le_bytes()) {
        hash ^= u64::from(byte);
        hash = hash.wrapping_mul(0x0000_0100_0000_01b3);
    }
    let mut encoded = String::with_capacity(16);
    write!(&mut encoded, "{hash:016x}").expect("writing to String cannot fail");
    encoded
}

#[cfg(test)]
mod tests {
    use snaploom_platform_contract::{PhysicalRect, PhysicalSize};

    use super::*;

    fn geometry() -> DisplayGeometry {
        DisplayGeometry {
            global_points: PointRect {
                x: 0.0,
                y: 0.0,
                width: 100.0,
                height: 100.0,
            },
            content_points: PointRect {
                x: 0.0,
                y: 0.0,
                width: 100.0,
                height: 100.0,
            },
            physical_size: PhysicalSize {
                width: 200,
                height: 200,
            },
        }
    }

    fn sck(id: u32) -> SckWindowRecord {
        SckWindowRecord {
            window_id: id,
            owner_pid: 7,
            owner_bundle: Some("com.example.editor".into()),
            layer: 0,
            on_screen: true,
            frame: PointRect {
                x: 10.25,
                y: 20.25,
                width: 30.25,
                height: 40.25,
            },
        }
    }

    fn cg(id: u32) -> CgWindowRecord {
        CgWindowRecord {
            window_id: id,
            owner_pid: 7,
            layer: 0,
            alpha: 1.0,
            on_screen: true,
        }
    }

    #[test]
    fn uses_cg_order_and_sck_geometry_without_exposing_titles() {
        let windows = merge_windows(
            vec![sck(2), sck(1)],
            &[cg(1), cg(2)],
            99,
            geometry(),
            &[3; 16],
        );
        assert_eq!(windows.len(), 2);
        assert_eq!(windows[0].z_order, 0);
        assert_eq!(windows[1].z_order, 1);
        assert_eq!(
            windows[0].bounds,
            PhysicalRect {
                x: 20,
                y: 40,
                width: 61,
                height: 81
            }
        );
        assert_eq!(windows[0].stable_id.len(), 16);
        assert_ne!(windows[0].stable_id, windows[1].stable_id);
    }

    #[test]
    fn conservatively_filters_self_system_layer_alpha_and_missing_cg_rows() {
        let mut self_owned = sck(1);
        self_owned.owner_pid = 99;
        let mut system = sck(2);
        system.owner_bundle = Some("com.apple.dock".into());
        let mut layered = sck(3);
        layered.layer = 1;
        let mut invisible = cg(4);
        invisible.alpha = 0.0;
        let windows = merge_windows(
            vec![self_owned, system, layered, sck(4), sck(5)],
            &[cg(1), cg(2), cg(3), invisible],
            99,
            geometry(),
            &[0; 16],
        );
        assert!(windows.is_empty());
    }

    #[test]
    fn keeps_ids_stable_only_within_same_secret() {
        let a = merge_windows(vec![sck(1)], &[cg(1)], 99, geometry(), &[1; 16]);
        let b = merge_windows(vec![sck(1)], &[cg(1)], 99, geometry(), &[1; 16]);
        let c = merge_windows(vec![sck(1)], &[cg(1)], 99, geometry(), &[2; 16]);
        assert_eq!(a[0].stable_id, b[0].stable_id);
        assert_ne!(a[0].stable_id, c[0].stable_id);
    }
}
