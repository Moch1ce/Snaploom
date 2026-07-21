use prost::Message;
use snaploom_capture_protocol::{
    Frame, FrameDecoder, FrameType, Hello, PngAccumulator, ProtocolRange, WireError,
    negotiate_version,
};

fn tiny_png() -> Vec<u8> {
    vec![
        0x89, b'P', b'N', b'G', 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, b'I', b'H', b'D', b'R', 0, 0,
        0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0, 0, 0, 0,
    ]
}

#[test]
fn decoder_accepts_every_partial_io_boundary() {
    let bytes = Frame::new(FrameType::Hello, vec![1, 2, 3, 4])
        .unwrap()
        .encode();
    for split in 0..bytes.len() {
        let mut decoder = FrameDecoder::default();
        assert!(decoder.push(&bytes[..split]).unwrap().is_empty());
        let frames = decoder.push(&bytes[split..]).unwrap();
        assert_eq!(
            frames,
            vec![Frame::new(FrameType::Hello, vec![1, 2, 3, 4]).unwrap()]
        );
    }
}

#[test]
fn decoder_rejects_oversized_payload_before_allocation() {
    let mut header = *b"SLCP\x01\x00\x01\x00\x00\x00\x10\x00";
    header[8..12].copy_from_slice(&(1_048_577_u32).to_le_bytes());
    assert_eq!(
        FrameDecoder::default().push(&header),
        Err(WireError::FrameTooLarge)
    );
}

#[test]
fn decoder_rejects_invalid_headers_and_accepts_multiple_frames() {
    let valid = Frame::new(FrameType::Hello, vec![7]).unwrap().encode();
    let mut invalid_magic = valid.clone();
    invalid_magic[0] = b'X';
    assert_eq!(
        FrameDecoder::default().push(&invalid_magic),
        Err(WireError::InvalidMagic)
    );
    let mut invalid_version = valid.clone();
    invalid_version[4] = 2;
    assert_eq!(
        FrameDecoder::default().push(&invalid_version),
        Err(WireError::UnsupportedFramingVersion)
    );
    let mut invalid_type = valid.clone();
    invalid_type[6..8].copy_from_slice(&99_u16.to_le_bytes());
    assert_eq!(
        FrameDecoder::default().push(&invalid_type),
        Err(WireError::UnknownFrameType)
    );

    let bytes = [valid.clone(), valid].concat();
    assert_eq!(FrameDecoder::default().push(&bytes).unwrap().len(), 2);
}

#[test]
fn png_stream_requires_contiguous_offsets_and_matching_ihdr() {
    let png = tiny_png();
    let mut receiver = PngAccumulator::begin(png.len() as u64, 1, 1).unwrap();
    assert_eq!(receiver.push(1, &png), Err(WireError::NonContiguousChunk));
    receiver.push(0, &png[..16]).unwrap();
    receiver.push(16, &png[16..]).unwrap();
    let result = receiver.finish().unwrap();
    assert_eq!(result.as_slice(), png);
}

#[test]
fn png_stream_rejects_empty_oversized_incomplete_and_mismatched_results() {
    let png = tiny_png();
    let mut receiver = PngAccumulator::begin(png.len() as u64, 1, 1).unwrap();
    assert_eq!(receiver.push(0, &[]), Err(WireError::InvalidChunk));
    receiver.push(0, &png[..16]).unwrap();
    assert!(matches!(receiver.finish(), Err(WireError::IncompletePng)));

    let mut receiver = PngAccumulator::begin(png.len() as u64, 2, 1).unwrap();
    receiver.push(0, &png).unwrap();
    assert!(matches!(receiver.finish(), Err(WireError::InvalidPng)));
    assert_eq!(
        PngAccumulator::begin(134_217_729, 1, 1).unwrap_err(),
        WireError::PngTooLarge
    );
}

#[test]
fn version_negotiation_selects_highest_shared_minor() {
    let client = ProtocolRange::new(1, 2, 5).unwrap();
    let host = ProtocolRange::new(1, 3, 4).unwrap();
    assert_eq!(negotiate_version(client, host, 0b101, 0b111).unwrap(), 4);
    assert_eq!(
        negotiate_version(client, ProtocolRange::new(2, 2, 5).unwrap(), 0, 0),
        Err(WireError::ProtocolIncompatible)
    );
}

#[test]
fn committed_hello_fixture_remains_decodable() {
    let text = include_str!("fixtures/hello-v1.hex").trim();
    let bytes = (0..text.len())
        .step_by(2)
        .map(|index| u8::from_str_radix(&text[index..index + 2], 16).unwrap())
        .collect::<Vec<_>>();
    let hello = Hello::decode(bytes.as_slice()).unwrap();
    assert_eq!(hello.protocol_major, 1);
    assert_eq!(hello.client_nonce, vec![1; 32]);
    assert_eq!(hello.sdk_semver, "0.1.0");
    assert_eq!(hello.requested_capabilities, 1);
}
