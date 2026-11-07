"""Reads IL2CPP tables straight from GameAssembly.dll: field offsets and type-table entries."""
import struct

BASE = 0x180000000
CLASS_KINDS = (0x11, 0x12)  # IL2CPP_TYPE_VALUETYPE, IL2CPP_TYPE_CLASS


class Binary:
    def __init__(self, path, types_count_field):
        self.data = open(path, "rb").read()
        pe = struct.unpack_from("<I", self.data, 0x3c)[0]
        count, optional = struct.unpack_from("<xxHxxxxxxxxxxxxH", self.data, pe + 4)
        self.sections = []
        for i in range(count):
            _, rva, size, raw = struct.unpack_from("<IIII", self.data, pe + 24 + optional + i * 40 + 8)
            self.sections.append((rva, size, raw))
        # MetadataRegistration: typesCount, types, methodSpecsCount, methodSpecs, fieldOffsetsCount, fieldOffsets.
        self.types = self.u64(types_count_field + 0x8)
        self.field_offsets = self.u64(types_count_field + 0x28)

    def offset(self, address):
        rva = address - BASE
        for start, size, raw in self.sections:
            if start <= rva < start + size:
                return raw + rva - start
        return None

    def u64(self, address):
        at = self.offset(address)
        return struct.unpack_from("<Q", self.data, at)[0] if at is not None else 0

    def type_definition(self, type_index):
        """The type definition a type-table entry names, or None for arrays, generics and the like."""
        entry = self.u64(self.types + type_index * 8)
        at = self.offset(entry) if entry else None
        if at is None or self.data[at + 10] not in CLASS_KINDS:
            return None
        return struct.unpack_from("<Q", self.data, at)[0]

    def offsets_of(self, type_definition, field_count):
        table = self.u64(self.field_offsets + type_definition * 8)
        at = self.offset(table) if table else None
        if at is None:
            return [None] * field_count
        return list(struct.unpack_from(f"<{field_count}i", self.data, at))
