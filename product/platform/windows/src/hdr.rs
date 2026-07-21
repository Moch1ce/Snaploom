use snaploom_platform_contract::PlatformError;

pub(crate) fn rgba16f_to_bgra8(
    source: &[u8],
    row_pitch: usize,
    width: usize,
    height: usize,
) -> Result<Vec<u8>, PlatformError> {
    let source_row_bytes = width
        .checked_mul(8)
        .ok_or(PlatformError::PixelConversionFailed)?;
    let output_row_bytes = width
        .checked_mul(4)
        .ok_or(PlatformError::PixelConversionFailed)?;
    if row_pitch < source_row_bytes
        || source.len()
            < row_pitch
                .checked_mul(height)
                .ok_or(PlatformError::PixelConversionFailed)?
    {
        return Err(PlatformError::PixelConversionFailed);
    }
    let content_peak = sc_rgb_content_peak(source, row_pitch, width, height);
    let mut output = vec![0_u8; output_row_bytes * height];
    for row in 0..height {
        let source_row = &source[row * row_pitch..row * row_pitch + source_row_bytes];
        let output_row = &mut output[row * output_row_bytes..(row + 1) * output_row_bytes];
        for column in 0..width {
            let input = column * 8;
            let red = half_to_f32(u16::from_le_bytes([
                source_row[input],
                source_row[input + 1],
            ]));
            let green = half_to_f32(u16::from_le_bytes([
                source_row[input + 2],
                source_row[input + 3],
            ]));
            let blue = half_to_f32(u16::from_le_bytes([
                source_row[input + 4],
                source_row[input + 5],
            ]));
            let target = column * 4;
            output_row[target] = linear_sc_rgb_to_srgb8(blue, content_peak);
            output_row[target + 1] = linear_sc_rgb_to_srgb8(green, content_peak);
            output_row[target + 2] = linear_sc_rgb_to_srgb8(red, content_peak);
            output_row[target + 3] = 255;
        }
    }
    Ok(output)
}

fn sc_rgb_content_peak(source: &[u8], row_pitch: usize, width: usize, height: usize) -> f32 {
    let mut peak = 1.0_f32;
    for row in 0..height {
        let source_row = &source[row * row_pitch..row * row_pitch + width * 8];
        for component in source_row.chunks_exact(2) {
            let value = half_to_f32(u16::from_le_bytes([component[0], component[1]]));
            if value.is_finite() {
                peak = peak.max(value);
            }
        }
    }
    peak
}

fn linear_sc_rgb_to_srgb8(value: f32, content_peak: f32) -> u8 {
    let linear = if value.is_finite() {
        value.max(0.0)
    } else {
        0.0
    };
    // Preserve SDR reference white exactly when the frame has no HDR content.
    // When a real highlight is present, reserve a small output shoulder so the
    // final 8-bit sRGB image retains highlight ordering instead of hard clipping.
    let mapped = if content_peak <= 1.0 || linear <= 0.75 {
        linear
    } else {
        0.75 + 0.25 * (1.0 - (-(linear - 0.75) / 0.25).exp())
    }
    .clamp(0.0, 1.0);
    let encoded = if mapped <= 0.003_130_8 {
        12.92 * mapped
    } else {
        1.055 * mapped.powf(1.0 / 2.4) - 0.055
    };
    (encoded * 255.0).round().clamp(0.0, 255.0) as u8
}

fn half_to_f32(bits: u16) -> f32 {
    let sign = u32::from(bits & 0x8000) << 16;
    let exponent = (bits >> 10) & 0x1f;
    let mantissa = u32::from(bits & 0x03ff);
    let float_bits = match exponent {
        0 if mantissa == 0 => sign,
        0 => {
            let mut mantissa = mantissa;
            let mut exponent = 113_u32;
            while mantissa & 0x0400 == 0 {
                mantissa <<= 1;
                exponent -= 1;
            }
            sign | (exponent << 23) | ((mantissa & 0x03ff) << 13)
        }
        0x1f => sign | 0x7f80_0000 | (mantissa << 13),
        _ => sign | ((u32::from(exponent) + 112) << 23) | (mantissa << 13),
    };
    f32::from_bits(float_bits)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn converts_half_float_primaries_to_opaque_bgra() {
        let rgba = [
            0x00, 0x3c, // R = 1.0
            0x00, 0x00, // G = 0.0
            0x00, 0x38, // B = 0.5
            0x00, 0x3c, // A is ignored for display capture
        ];

        let bgra = rgba16f_to_bgra8(&rgba, 8, 1, 1).unwrap();

        assert_eq!(bgra[3], 255);
        assert_eq!(bgra[2], 255);
        assert!(bgra[2] > bgra[0]);
        assert_eq!(bgra[1], 0);
    }

    #[test]
    fn smooth_shoulder_preserves_highlight_order_without_clipping_every_value() {
        let white = linear_sc_rgb_to_srgb8(1.0, 8.0);
        let highlight = linear_sc_rgb_to_srgb8(2.0, 8.0);
        let extreme = linear_sc_rgb_to_srgb8(8.0, 8.0);

        assert!(white < highlight);
        assert!(highlight <= extreme);
        assert_eq!(extreme, 255);
    }

    #[test]
    fn sdr_reference_white_is_not_dimmed_without_hdr_highlights() {
        assert_eq!(linear_sc_rgb_to_srgb8(1.0, 1.0), 255);
    }

    #[test]
    fn validates_row_pitch_and_source_length() {
        assert_eq!(
            rgba16f_to_bgra8(&[0; 7], 7, 1, 1),
            Err(PlatformError::PixelConversionFailed)
        );
    }
}
