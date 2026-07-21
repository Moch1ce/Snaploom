use std::hash::{Hash, Hasher};

use snaploom_platform_contract::{PhysicalRect, WindowCandidate};

#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct RawWindow {
    pub(crate) native_id: isize,
    pub(crate) z_order: u32,
    pub(crate) process_id: u32,
    pub(crate) bounds: PhysicalRect,
    pub(crate) visible: bool,
    pub(crate) minimized: bool,
    pub(crate) child: bool,
    pub(crate) owned: bool,
    pub(crate) tool_window: bool,
    pub(crate) no_activate: bool,
    pub(crate) click_through: bool,
    pub(crate) cloaked: bool,
    pub(crate) transparent: bool,
    pub(crate) system_ui: bool,
}

#[must_use]
pub(crate) fn normalize_windows(
    windows: impl IntoIterator<Item = RawWindow>,
    own_process_id: u32,
    display: PhysicalRect,
    session_secret: &[u8; 16],
) -> Vec<WindowCandidate> {
    windows
        .into_iter()
        .filter(|window| {
            window.visible
                && !window.minimized
                && !window.child
                && !window.owned
                && !window.tool_window
                && !window.no_activate
                && !window.click_through
                && !window.cloaked
                && !window.transparent
                && !window.system_ui
                && window.process_id != own_process_id
        })
        .filter_map(|window| {
            intersect(window.bounds, display).map(|bounds| WindowCandidate {
                stable_id: opaque_id(session_secret, window.native_id),
                z_order: window.z_order,
                bounds: PhysicalRect {
                    x: bounds.x - display.x,
                    y: bounds.y - display.y,
                    width: bounds.width,
                    height: bounds.height,
                },
            })
        })
        .collect()
}

fn opaque_id(session_secret: &[u8; 16], native_id: isize) -> String {
    let mut hasher = std::collections::hash_map::DefaultHasher::new();
    session_secret.hash(&mut hasher);
    native_id.hash(&mut hasher);
    format!("{:016x}", hasher.finish())
}

fn intersect(left: PhysicalRect, right: PhysicalRect) -> Option<PhysicalRect> {
    let left_x2 = i64::from(left.x) + i64::from(left.width);
    let left_y2 = i64::from(left.y) + i64::from(left.height);
    let right_x2 = i64::from(right.x) + i64::from(right.width);
    let right_y2 = i64::from(right.y) + i64::from(right.height);
    let x1 = i64::from(left.x).max(i64::from(right.x));
    let y1 = i64::from(left.y).max(i64::from(right.y));
    let x2 = left_x2.min(right_x2);
    let y2 = left_y2.min(right_y2);
    if x2 <= x1 || y2 <= y1 {
        return None;
    }
    Some(PhysicalRect {
        x: i32::try_from(x1).ok()?,
        y: i32::try_from(y1).ok()?,
        width: u32::try_from(x2 - x1).ok()?,
        height: u32::try_from(y2 - y1).ok()?,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ordinary(native_id: isize, z_order: u32, bounds: PhysicalRect) -> RawWindow {
        RawWindow {
            native_id,
            z_order,
            process_id: 100,
            bounds,
            visible: true,
            minimized: false,
            child: false,
            owned: false,
            tool_window: false,
            no_activate: false,
            click_through: false,
            cloaked: false,
            transparent: false,
            system_ui: false,
        }
    }

    #[test]
    fn clips_candidates_to_a_negative_origin_display_without_reordering() {
        let display = PhysicalRect {
            x: -1920,
            y: -180,
            width: 1920,
            height: 1080,
        };
        let windows = [
            ordinary(
                1,
                0,
                PhysicalRect {
                    x: -2000,
                    y: -100,
                    width: 300,
                    height: 200,
                },
            ),
            ordinary(
                2,
                1,
                PhysicalRect {
                    x: -500,
                    y: 700,
                    width: 800,
                    height: 400,
                },
            ),
        ];

        let result = normalize_windows(windows, 999, display, &[1; 16]);

        assert_eq!(result.len(), 2);
        assert_eq!(result[0].z_order, 0);
        assert_eq!(
            result[0].bounds,
            PhysicalRect {
                x: 0,
                y: 80,
                width: 220,
                height: 200,
            }
        );
        assert_eq!(
            result[1].bounds,
            PhysicalRect {
                x: 1420,
                y: 880,
                width: 500,
                height: 200,
            }
        );
        assert_ne!(result[0].stable_id, "1");
    }

    #[test]
    fn rejects_each_forbidden_window_class_and_own_process() {
        let display = PhysicalRect {
            x: 0,
            y: 0,
            width: 100,
            height: 100,
        };
        let mut windows = Vec::new();
        for index in 0..11 {
            windows.push(ordinary(
                index,
                index as u32,
                PhysicalRect {
                    x: 0,
                    y: 0,
                    width: 50,
                    height: 50,
                },
            ));
        }
        windows[0].visible = false;
        windows[1].minimized = true;
        windows[2].child = true;
        windows[3].owned = true;
        windows[4].tool_window = true;
        windows[5].no_activate = true;
        windows[6].click_through = true;
        windows[7].cloaked = true;
        windows[8].transparent = true;
        windows[9].system_ui = true;
        windows[10].process_id = 777;

        assert!(normalize_windows(windows, 777, display, &[1; 16]).is_empty());
    }

    #[test]
    fn stable_ids_are_session_scoped() {
        let display = PhysicalRect {
            x: 0,
            y: 0,
            width: 100,
            height: 100,
        };
        let window = ordinary(42, 0, display);

        let first = normalize_windows([window.clone()], 0, display, &[1; 16]);
        let repeated = normalize_windows([window.clone()], 0, display, &[1; 16]);
        let next_session = normalize_windows([window], 0, display, &[2; 16]);

        assert_eq!(first[0].stable_id, repeated[0].stable_id);
        assert_ne!(first[0].stable_id, next_session[0].stable_id);
    }
}
