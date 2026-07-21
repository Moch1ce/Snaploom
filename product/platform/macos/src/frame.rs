use snaploom_platform_contract::PlatformError;
use zeroize::Zeroize;

pub(crate) struct SensitiveBgraFrame(Vec<u8>);

impl SensitiveBgraFrame {
    pub(crate) fn into_bytes(mut self) -> Vec<u8> {
        std::mem::take(&mut self.0)
    }

    #[cfg(test)]
    fn as_slice(&self) -> &[u8] {
        &self.0
    }
}

impl Drop for SensitiveBgraFrame {
    fn drop(&mut self) {
        self.0.zeroize();
    }
}

pub(crate) fn copy_bgra_rows(
    source: &[u8],
    width: u32,
    height: u32,
    source_stride: usize,
) -> Result<(SensitiveBgraFrame, u32), PlatformError> {
    let row_bytes = usize::try_from(width)
        .ok()
        .and_then(|width| width.checked_mul(4))
        .ok_or(PlatformError::PixelConversionFailed)?;
    let height = usize::try_from(height).map_err(|_| PlatformError::PixelConversionFailed)?;
    if row_bytes == 0 || height == 0 || source_stride < row_bytes {
        return Err(PlatformError::PixelConversionFailed);
    }
    let source_length = source_stride
        .checked_mul(height)
        .ok_or(PlatformError::PixelConversionFailed)?;
    if source.len() < source_length {
        return Err(PlatformError::PixelConversionFailed);
    }
    let output_length = row_bytes
        .checked_mul(height)
        .ok_or(PlatformError::PixelConversionFailed)?;
    let mut output = Vec::new();
    output
        .try_reserve_exact(output_length)
        .map_err(|_| PlatformError::PixelConversionFailed)?;
    for row in 0..height {
        let start = row * source_stride;
        output.extend_from_slice(&source[start..start + row_bytes]);
    }
    Ok((
        SensitiveBgraFrame(output),
        u32::try_from(row_bytes).map_err(|_| PlatformError::PixelConversionFailed)?,
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn copies_top_to_bottom_and_drops_stride_padding() {
        let source = [
            1, 2, 3, 4, 5, 6, 7, 8, 99, 99, 99, 99, 9, 10, 11, 12, 13, 14, 15, 16, 88, 88, 88, 88,
        ];
        let (frame, stride) = copy_bgra_rows(&source, 2, 2, 12).unwrap();
        assert_eq!(stride, 8);
        assert_eq!(
            frame.as_slice(),
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]
        );
    }

    #[test]
    fn rejects_short_or_narrow_buffers() {
        assert!(matches!(
            copy_bgra_rows(&[0; 7], 2, 1, 8),
            Err(PlatformError::PixelConversionFailed)
        ));
        assert!(matches!(
            copy_bgra_rows(&[0; 8], 2, 1, 4),
            Err(PlatformError::PixelConversionFailed)
        ));
    }

    #[test]
    fn preserves_four_corner_bgra_orientation_and_premultiplied_alpha() {
        // top-left red, top-right half-alpha premultiplied green,
        // bottom-left blue, bottom-right black.
        let source = [0, 0, 255, 255, 0, 128, 0, 128, 255, 0, 0, 255, 0, 0, 0, 255];
        let (frame, stride) = copy_bgra_rows(&source, 2, 2, 8).unwrap();
        assert_eq!(stride, 8);
        assert_eq!(frame.as_slice(), source);
    }
}
