package com.nags.operations.data

/** Match the server's signature envelope policy before publishing any local form intent. */
fun validateFlightSignatureBytes(content: ByteArray) {
    check(content.size in 45..2_097_152 && isFlightSignaturePng(content)) {
        "The customer signature must be a PNG image of at most 2 MB."
    }
}

private fun isFlightSignaturePng(content: ByteArray): Boolean {
    fun unsignedInt(offset: Int): Long = (0..3).fold(0L) { value, index ->
        (value shl 8) or (content[offset + index].toLong() and 255)
    }
    fun chunkType(offset: Int, type: String): Boolean = type.indices.all {
        content[offset + 4 + it] == type[it].code.toByte()
    }
    if (!content.copyOfRange(0, 8).contentEquals(byteArrayOf(137.toByte(), 80, 78, 71, 13, 10, 26, 10)) ||
        unsignedInt(8) != 13L || !chunkType(8, "IHDR") || unsignedInt(16) == 0L || unsignedInt(20) == 0L) return false
    var offset = 8
    var hasImageData = false
    while (offset <= content.size - 12) {
        val length = unsignedInt(offset)
        if (length > content.size - offset - 12) return false
        if (chunkType(offset, "IDAT")) hasImageData = hasImageData || length > 0
        if (chunkType(offset, "IEND")) return length == 0L && hasImageData && offset + 12 == content.size
        offset += length.toInt() + 12
    }
    return false
}
