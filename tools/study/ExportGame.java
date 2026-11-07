// Ghidra headless post-script: names every IL2CPP method from methods.tsv, then decompiles the flagged ones
// into one .c file per class. Run with -noanalysis: the decompiler does not need Ghidra's auto-analysis.
// Args: <methods.tsv> <out-dir>. Output is decompiled game code: keep it outside the repo, never distribute it.
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.SourceType;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public class ExportGame extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        Path out = Path.of(args[1]);
        Files.createDirectories(out);
        List<String[]> rows = new ArrayList<>();
        for (String line : Files.readAllLines(Path.of(args[0]), StandardCharsets.UTF_8)) rows.add(line.split("\t", -1));

        long base = currentProgram.getImageBase().getOffset();
        Map<Long, String> firstName = new LinkedHashMap<>();
        for (String[] r : rows) {
            long rva = Long.decode(r[0]);
            String name = (r[2] + "$$" + r[3]).replaceAll("[^A-Za-z0-9_$.+]", "_");
            Address a = toAddr(base + rva);
            if (firstName.putIfAbsent(rva, name) != null) continue;
            disassemble(a);
            Function f = getFunctionAt(a);
            if (f == null) f = createFunction(a, name);
            if (f != null) f.setName(name, SourceType.USER_DEFINED);
        }
        println("Named " + firstName.size() + " functions");

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        Map<String, List<String[]>> byClass = new LinkedHashMap<>();
        for (String[] r : rows) if (r[1].equals("1")) byClass.computeIfAbsent(r[2], k -> new ArrayList<>()).add(r);

        int done = 0, failed = 0, classes = 0;
        for (Map.Entry<String, List<String[]>> e : byClass.entrySet()) {
            Path file = out.resolve(e.getKey().replaceAll("[^A-Za-z0-9_.+-]", "_") + ".c");
            try (PrintWriter w = new PrintWriter(file.toFile(), StandardCharsets.UTF_8)) {
                for (String[] r : e.getValue()) {
                    long rva = Long.decode(r[0]);
                    w.println("// " + r[4] + "  [rva 0x" + Long.toHexString(rva) + "]");
                    String shared = firstName.get(rva);
                    if (!shared.equals((r[2] + "$$" + r[3]).replaceAll("[^A-Za-z0-9_$.+]", "_"))) w.println("// shares its compiled body with " + shared);
                    Function f = getFunctionAt(toAddr(base + rva));
                    DecompileResults result = f == null ? null : decompiler.decompileFunction(f, 60, monitor);
                    if (result != null && result.decompileCompleted()) { w.println(result.getDecompiledFunction().getC()); done++; }
                    else { w.println("// DECOMPILE FAILED\n"); failed++; }
                }
            }
            if (++classes % 50 == 0) println("Classes " + classes + "/" + byClass.size() + ", methods " + done + ", failed " + failed);
        }
        decompiler.dispose();
        println("EXPORT DONE classes=" + classes + " methods=" + done + " failed=" + failed);
    }
}
