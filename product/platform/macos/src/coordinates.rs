use snaploom_platform_contract::{PhysicalPoint, PhysicalRect, PhysicalSize, PlatformError};

#[derive(Debug, Clone, Copy, PartialEq)]
pub(crate) struct PointRect {
    pub(crate) x: f64,
    pub(crate) y: f64,
    pub(crate) width: f64,
    pub(crate) height: f64,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub(crate) struct DisplayGeometry {
    pub(crate) global_points: PointRect,
    pub(crate) content_points: PointRect,
    pub(crate) physical_size: PhysicalSize,
}

impl DisplayGeometry {
    pub(crate) fn validate(self) -> Result<Self, PlatformError> {
        let values = [
            self.global_points.x,
            self.global_points.y,
            self.global_points.width,
            self.global_points.height,
            self.content_points.x,
            self.content_points.y,
            self.content_points.width,
            self.content_points.height,
        ];
        if values.iter().any(|value| !value.is_finite())
            || self.global_points.width <= 0.0
            || self.global_points.height <= 0.0
            || self.content_points.width <= 0.0
            || self.content_points.height <= 0.0
            || self.physical_size.width == 0
            || self.physical_size.height == 0
        {
            return Err(PlatformError::InvalidCaptureDescriptor);
        }
        Ok(self)
    }

    pub(crate) fn point_scale(self) -> (f64, f64) {
        (
            f64::from(self.physical_size.width) / self.content_points.width,
            f64::from(self.physical_size.height) / self.content_points.height,
        )
    }

    pub(crate) fn pointer_local(self, global: (f64, f64)) -> PhysicalPoint {
        let (scale_x, scale_y) = self.point_scale();
        PhysicalPoint {
            x: floor_i32((global.0 - self.content_points.x) * scale_x),
            y: floor_i32((global.1 - self.content_points.y) * scale_y),
        }
    }

    pub(crate) fn window_local(self, global: PointRect) -> Option<PhysicalRect> {
        let (scale_x, scale_y) = self.point_scale();
        let left = ((global.x - self.content_points.x) * scale_x).floor();
        let top = ((global.y - self.content_points.y) * scale_y).floor();
        let right = ((global.x + global.width - self.content_points.x) * scale_x).ceil();
        let bottom = ((global.y + global.height - self.content_points.y) * scale_y).ceil();
        let left = left.clamp(0.0, f64::from(self.physical_size.width));
        let top = top.clamp(0.0, f64::from(self.physical_size.height));
        let right = right.clamp(0.0, f64::from(self.physical_size.width));
        let bottom = bottom.clamp(0.0, f64::from(self.physical_size.height));
        if right <= left || bottom <= top {
            return None;
        }
        Some(PhysicalRect {
            x: floor_i32(left),
            y: floor_i32(top),
            width: ceil_u32(right - left),
            height: ceil_u32(bottom - top),
        })
    }
}

pub(crate) fn appkit_frame(
    global_points: PointRect,
    primary_height: f64,
) -> Result<PointRect, PlatformError> {
    if !primary_height.is_finite() || primary_height <= 0.0 {
        return Err(PlatformError::InvalidCaptureDescriptor);
    }
    Ok(PointRect {
        x: global_points.x,
        y: primary_height - global_points.y - global_points.height,
        width: global_points.width,
        height: global_points.height,
    })
}

fn floor_i32(value: f64) -> i32 {
    value
        .floor()
        .clamp(f64::from(i32::MIN), f64::from(i32::MAX)) as i32
}

fn ceil_u32(value: f64) -> u32 {
    value.ceil().clamp(0.0, f64::from(u32::MAX)) as u32
}

#[cfg(test)]
mod tests {
    use super::*;

    fn retina() -> DisplayGeometry {
        DisplayGeometry {
            global_points: PointRect {
                x: -1440.0,
                y: 0.0,
                width: 1440.0,
                height: 900.0,
            },
            content_points: PointRect {
                x: -1440.0,
                y: 0.0,
                width: 1440.0,
                height: 900.0,
            },
            physical_size: PhysicalSize {
                width: 2880,
                height: 1800,
            },
        }
    }

    #[test]
    fn maps_negative_origin_pointer_and_fractional_window_to_local_pixels() {
        let geometry = retina().validate().unwrap();
        assert_eq!(
            geometry.pointer_local((-1439.5, 1.25)),
            PhysicalPoint { x: 1, y: 2 }
        );
        assert_eq!(
            geometry.window_local(PointRect {
                x: -1440.25,
                y: 10.25,
                width: 100.5,
                height: 40.5
            }),
            Some(PhysicalRect {
                x: 0,
                y: 20,
                width: 201,
                height: 82
            })
        );
    }

    #[test]
    fn clamps_cross_display_windows_and_flips_appkit_y() {
        let geometry = retina();
        assert_eq!(
            geometry.window_local(PointRect {
                x: -1500.0,
                y: 850.0,
                width: 100.0,
                height: 100.0
            }),
            Some(PhysicalRect {
                x: 0,
                y: 1700,
                width: 80,
                height: 100
            })
        );
        assert_eq!(
            appkit_frame(geometry.global_points, 1080.0).unwrap(),
            PointRect {
                x: -1440.0,
                y: 180.0,
                width: 1440.0,
                height: 900.0
            }
        );
    }

    #[test]
    fn keeps_independent_xy_scales() {
        let geometry = DisplayGeometry {
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
                height: 150,
            },
        };
        assert_eq!(
            geometry.pointer_local((20.0, 20.0)),
            PhysicalPoint { x: 40, y: 30 }
        );
    }
}
