import com.intellij.openapi.editor.impl.EditorImpl
import com.jetbrains.rider.test.CompletionTestBase
import com.jetbrains.rider.test.scriptingApi.*
import org.testng.annotations.Test

@Test
class FSharpBasicCompletionTest : CompletionTestBase() {
    override fun getSolutionDirectoryName() = "CoreConsoleApp"
    override val restoreNuGetPackages = true

    @Test
    fun basicCompletion() {
        doTest {
            typeWithLatency("filt")
            waitForCompletion()
            completeWithTab()
        }
    }

    private fun doTest(test: EditorImpl.() -> Unit) {
        doTestWithDocuments {
            withCaret("Program.fs", "Program.fs") {
                wait(30000) // todo: add property to wait for FCS to become ready
                test()
            }
        }
    }
}