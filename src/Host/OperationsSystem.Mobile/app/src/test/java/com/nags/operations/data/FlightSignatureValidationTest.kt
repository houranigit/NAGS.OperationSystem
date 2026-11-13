package com.nags.operations.data

import java.util.Base64
import org.junit.Assert.assertThrows
import org.junit.Test

class FlightSignatureValidationTest {
    private val png = Base64.getDecoder().decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aPsYAAAAASUVORK5CYII=")

    @Test fun acceptsACompleteSignaturePng() {
        validateFlightSignatureBytes(png)
    }

    @Test fun rejectsTruncatedOrHeaderOnlySignaturesBeforeSave() {
        listOf(png.copyOf(8), png.copyOf(45), png.copyOf(png.size - 1)).forEach { invalid ->
            assertThrows(IllegalStateException::class.java) { validateFlightSignatureBytes(invalid) }
        }
    }

    @Test fun rejectsZeroDimensionsAndInvalidHeaderLength() {
        listOf(8, 16, 20).forEach { offset ->
            val invalid = png.copyOf().also { bytes -> (offset..offset + 3).forEach { bytes[it] = 0 } }
            assertThrows(IllegalStateException::class.java) { validateFlightSignatureBytes(invalid) }
        }
    }

    @Test fun rejectsImpossibleChunkLengthsWithoutOverflow() {
        val invalid = png.copyOf().also { bytes -> (33..36).forEach { bytes[it] = 255.toByte() } }
        assertThrows(IllegalStateException::class.java) { validateFlightSignatureBytes(invalid) }
    }

    @Test fun rejectsMissingImageDataOrTrailingBytes() {
        val withoutData = png.copyOf().also { bytes -> "tEXt".forEachIndexed { index, value -> bytes[37 + index] = value.code.toByte() } }
        listOf(withoutData, png + byteArrayOf(0)).forEach { invalid ->
            assertThrows(IllegalStateException::class.java) { validateFlightSignatureBytes(invalid) }
        }
    }

    @Test fun rejectsOversizedSignature() {
        val invalid = png.copyOf(2_097_153)
        assertThrows(IllegalStateException::class.java) { validateFlightSignatureBytes(invalid) }
    }
}
