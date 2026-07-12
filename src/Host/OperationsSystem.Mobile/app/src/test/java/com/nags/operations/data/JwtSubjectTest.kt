package com.nags.operations.data

import java.util.Base64
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class JwtSubjectTest {
    @Test
    fun extractsSubjectFromUrlSafeJwtPayload() {
        val token = jwt("""{"sub":"8a488cd5-86f8-44f4-84be-dad2f2b40551","name":"Ramp User"}""")

        assertEquals("8a488cd5-86f8-44f4-84be-dad2f2b40551", JwtSubject.fromAccessToken(token))
    }

    @Test
    fun rejectsMalformedOrOwnerlessTokens() {
        assertNull(JwtSubject.fromAccessToken("not-a-jwt"))
        assertNull(JwtSubject.fromAccessToken(jwt("""{"name":"Ramp User"}""")))
    }

    private fun jwt(payload: String): String {
        val encoder = Base64.getUrlEncoder().withoutPadding()
        val header = encoder.encodeToString("""{"alg":"none"}""".toByteArray())
        val body = encoder.encodeToString(payload.toByteArray())
        return "$header.$body.signature"
    }
}
