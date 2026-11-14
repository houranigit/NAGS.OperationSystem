package com.nags.operations.ui.account

import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class PasswordViewModelTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
    @After fun tearDown() = scope.cancel()

    @Test fun offlineValidationNeverQueuesCredentialsAndReconnectDoesNotSubmitThem() {
        val online = MutableStateFlow(false)
        var writes = 0
        val model = PasswordViewModel(online, { _, _ -> writes++ }, scope)
        model.current("current")
        model.replacement("NextPass1!")
        model.confirmation("NextPass1!")
        model.save()
        assertEquals(0, writes)
        assertEquals("current", model.state.value.current)
        online.value = true
        assertEquals(0, writes)
        model.save()
        assertEquals(1, writes)
        assertTrue(model.state.value.changed)
        assertEquals("", model.state.value.current)
        assertEquals("", model.state.value.replacement)
    }

    @Test fun invalidAndMismatchedPasswordsStopBeforeIdentityIsCalled() {
        var writes = 0
        val model = PasswordViewModel(MutableStateFlow(true), { _, _ -> writes++ }, scope)
        model.save()
        assertEquals(setOf(PasswordProblem.CurrentRequired, PasswordProblem.Policy, PasswordProblem.Confirmation),
            passwordProblems(model.state.value))
        model.current("current")
        model.replacement("alllower123!")
        model.confirmation("alllower123!")
        model.save()
        assertEquals(setOf(PasswordProblem.Policy), passwordProblems(model.state.value))
        model.replacement("NextPass1!")
        model.save()
        assertEquals(setOf(PasswordProblem.Confirmation), passwordProblems(model.state.value))
        assertEquals(0, writes)
    }

    @Test fun aFailedRequestRetainsEditsAndAllowsExplicitRetryWithoutClaimingSuccess() {
        var fail = true
        val model = PasswordViewModel(MutableStateFlow(true), { _, _ -> if (fail) throw IOException() }, scope)
        model.current("current")
        model.replacement("NextPass1!")
        model.confirmation("NextPass1!")
        model.save()
        assertFalse(model.state.value.changed)
        assertFalse(model.state.value.isSaving)
        assertNotNull(model.state.value.error)
        assertEquals("NextPass1!", model.state.value.replacement)
        fail = false
        model.save()
        assertTrue(model.state.value.changed)
        assertNull(model.state.value.error)
    }

    @Test fun oneInFlightRequestPreservesExactPasswordCharactersAndClearsSensitiveInputOnSuccess() {
        val response = CompletableDeferred<Unit>()
        val requests = mutableListOf<Pair<String, String>>()
        val model = PasswordViewModel(MutableStateFlow(true), { current, replacement ->
            requests += current to replacement
            response.await()
        }, scope)
        model.current(" current password ")
        model.replacement(" NextPass1! ")
        model.confirmation(" NextPass1! ")
        model.save()
        model.save()
        model.current("must not replace in-flight input")
        assertEquals(listOf(" current password " to " NextPass1! "), requests)
        assertTrue(model.state.value.isSaving)
        response.complete(Unit)
        assertTrue(model.state.value.changed)
        assertEquals("", model.state.value.current)
        assertEquals("", model.state.value.confirmation)
        model.save()
        assertEquals(1, requests.size)
    }
}
