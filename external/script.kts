#!/usr/bin/env kscript

@file:MavenRepository("local-rep","file:///proj/external/mylib" )

@file:DependsOn("de.matthid:dexlib2:1.0.0-local")

import java.io.*
import org.jf.dexlib2.dexbacked.*
import org.jf.dexlib2.dexbacked.reference.*
import org.jf.dexlib2.dexbacked.instruction.*
import org.jf.dexlib2.iface.instruction.*
import org.jf.dexlib2.iface.reference.*
import org.jf.dexlib2.dexbacked.raw.*
import org.jf.dexlib2.*

data class InstructionRef(val method : DexBackedMethodImplementation, val instruction : Instruction) {
}

class IndexedDex (dexFile: DexBackedDexFile) {
    
    val dexFile = dexFile
    val strCount = dexFile.getStringCount()

    fun findAllStrings(subStr : String) : List<StringReference> {
        return (0 .. strCount - 1).map {
            i -> DexBackedStringReference(dexFile, i)
        }.filter {
            sref -> sref.getString().contains(subStr)
        }
    }

    val stringToInstructionReferences : MutableMap<StringReference, MutableList<InstructionRef>> = mutableMapOf()
    val stringToTypeReferences : MutableMap<StringReference, MutableList<TypeReference>> = mutableMapOf()
    val stringToMethodReferences : MutableMap<StringReference, MutableList<DexBackedMethod>> = mutableMapOf()
    val typeToClassReferences : MutableMap<TypeReference, MutableList<DexBackedClassDef>> = mutableMapOf()

    fun <K, V>getList(map: MutableMap<K, MutableList<V>>, key : K) : List<V> {
        when (val l = map.get(key)) {
            null -> return listOf()
            else -> { 
                // warn if exists?
                return l
            }
        }
    }
        
    // build reverse reference tables
    init {

        fun <K, V>addOrUpdateList(map: MutableMap<K, MutableList<V>>, key : K, value: V) {
            when (val l = map.get(key)) {
                null -> map.put(key, mutableListOf(value))
                else -> { 
                    // warn if exists?
                    l.add(value)
                }
            }
        }

        
        fun indexRef(op: InstructionRef, ref: Reference) {
            when (ref) {
                is StringReference -> addOrUpdateList(stringToInstructionReferences, ref, op)
                else -> {}
            }
        }

        fun indexOp(method: DexBackedMethodImplementation, op: Instruction) {
            val ref = InstructionRef(method, op)
            when (op) {
                is DualReferenceInstruction -> {
                    indexRef (ref, op.getReference())
                    indexRef (ref, op.getReference2())
                }
                is ReferenceInstruction -> indexRef (ref, op.getReference())
                else -> {}
            }
        }

        fun indexMethod(method: DexBackedMethod) {
            addOrUpdateList(stringToMethodReferences, method.getNameRef(), method)
            when (val impl = method.getImplementation()){
                null -> {}
                else -> {
                    for (instr in impl.getInstructions()){
                        indexOp(impl, instr)
                    }
                }
            }
        }

        fun indexClass(clazz : DexBackedClassDef) {
            val typeDef = clazz.getTypeRef()
            addOrUpdateList(typeToClassReferences, typeDef, clazz)
            addOrUpdateList(stringToTypeReferences, typeDef.getDescriptorRef(), typeDef)

            for (meth in clazz.getDirectMethods(false)) {
                indexMethod(meth)
            }
            for (meth in clazz.getVirtualMethods(false)) {
                indexMethod(meth)
            }
            
            // fields and other stuff?
        }

        for (clazz in dexFile.getClasses()) {
            indexClass(clazz)
        }
    }

    fun findStringRefInInstructions (str : StringReference) : List<InstructionRef> {
        return getList(stringToInstructionReferences, str)
    }
    fun findStringRefInTypes (str : StringReference) : List<TypeReference> {
        return getList(stringToTypeReferences, str)
    }
    fun findStringRefInMethods (str : StringReference) : List<DexBackedMethod> {
        return getList(stringToMethodReferences, str)
    }
    fun findClassForTypeRef (str : TypeReference) : List<DexBackedClassDef> {
        return getList(typeToClassReferences, str)
    }

    fun getStringRef(ref:Int) : StringReference {
        return DexBackedStringReference(dexFile, ref)
    }

    fun <T>findString(finder: (StringReference) -> List<T>, sub:String) : List<Pair<StringReference, T>> {
        return findAllStrings(sub).flatMap { ref -> finder(ref).map { item -> Pair(ref, item) } }
    }

    fun printUsages (sub:String) {
        for (ref in findAllStrings(sub)) {
            println("string '${ref.getString()}'")
            for (instr in findStringRefInInstructions(ref)) {
                val method = instr.method.method
                println("-> used in instruction '${instr.instruction.getOpcode()}' of method '${method.getName()}' of class '${method.classDef.getType()}'  ")
            }
            
            for (typ in findStringRefInTypes(ref)) {
                println("-> used in type name '${typ.getType()}'")
            }
            
            for (method in findStringRefInMethods(ref)) {
                println("-> used in method name '${method.getName()}'")
            }
        }
    }

    fun explainClass (clazz : String){
        for ((strRef, typeRef) in findString(::findStringRefInTypes, clazz)){
            println("found '${strRef.getString()}'")
            for (clazzRef in findClassForTypeRef(typeRef)){
                
                println("matched class '${clazzRef.getType()}' -> Inherited from '${clazzRef.getSuperclass()}', defined in '${clazzRef.getSourceFile()}'")
                for (interfaze in clazzRef.getInterfaces()) {
                    println(" -> implements ${interfaze}")
                }
                for (annotate in clazzRef.getAnnotations()) {
                    println(" -> annotation ${annotate.getType()}")
                }
                for (staticField in clazzRef.getStaticFields(false)) {
                    println(" -> static field '${staticField.getName()}' of type '${staticField.getType()}'")
                }
                for (instanceField in clazzRef.getInstanceFields(false)) {
                    println(" -> field '${instanceField.getName()}' of type '${instanceField.getType()}'")
                }
                for (directMethod in clazzRef.getDirectMethods(false)) {
                    println(" -> direct method '${directMethod.getName()}' with return type '${directMethod.getReturnType()}'")
                }

                for (virtualMethod in clazzRef.getVirtualMethods(false)) {
                    println(" -> virtual method '${virtualMethod.getName()}' with return type '${virtualMethod.getReturnType()}'")
                }
            }
        }
    }

    fun explainMethod(clazzName:String, methodName:String) {
        fun printReference(ref: Reference) : String {
            when (ref) {
                is StringReference -> return "(stringRef '${ref.getString()}')"
                else -> return "otherRef"
            }
        }
        fun handleFunction(method:DexBackedMethod) {
            println(" -> method '${method.getName()}' with return type '${method.getReturnType()}'")

            when (val impl = method.getImplementation()){
                null -> println(" -> Implementation missing...")
                else -> {
                    val startInstrOffset = impl.codeOffset + CodeItem.INSTRUCTION_START_OFFSET
                    for ((i, instr) in impl.getInstructions().withIndex()){
                        val intrOffset = (instr as DexBackedInstruction).instructionStart
                        val byteOffset = intrOffset - startInstrOffset
                        when (instr) {
                            is DualReferenceInstruction -> {
                                println("$i: [$byteOffset] ${instr.getOpcode()} ${printReference(instr.getReference())} ${printReference(instr.getReference())}")
                            }
                            is ReferenceInstruction -> 
                                println("$i: [$byteOffset] ${instr.getOpcode()} ${printReference(instr.getReference())}")
                            else -> println("$i: [$byteOffset] ${instr.getOpcode()}")
                        }
                    }
                }
            }
        }

        for ((strRef, typeRef) in findString(::findStringRefInTypes, clazzName)){
            for (clazzRef in findClassForTypeRef(typeRef)){
                println("found '${strRef.getString()}'")
                
                for (directMethod in clazzRef.getDirectMethods(false)) {
                    if (directMethod.getName().contains(methodName)){
                        handleFunction(directMethod)
                    }
                }

                for (virtualMethod in clazzRef.getVirtualMethods(false)) {
                    if (virtualMethod.getName().contains(methodName)){
                        handleFunction(virtualMethod)
                    }
                }
            }
        }
    }
}

val initialFile = File("/proj/external/apk/i lamp_v1.48_apkmonk.com.extracted/classes.dex")
val targetStream = BufferedInputStream(FileInputStream(initialFile))
val dexFile = DexBackedDexFile.fromInputStream(null, targetStream)



val file2 = File("/proj/external/apk/i lamp_v1.48_apkmonk.com.extracted/classes2.dex")
val targetStream2 = BufferedInputStream(FileInputStream(file2))
val dexFile2 = DexBackedDexFile.fromInputStream(null, targetStream2)


>>> for ((i, ref) in findAllStrings("ClassLoader").withIndex()) {
        println("${i}: [${(ref as DexBackedStringReference).stringIndex}] -> ${ref}") }


>>> for ((strRef, opRef) in findString(::findStringRefInInstructions, "ClassLoader")) {
        val method = opRef.method.method
        println("string '${strRef.getString()}'")
        println("-> used in method '${method.getName()}' ")
        println("  -> of class '${method.classDef.getType()}' ") }

val clazzes = dexFile.getClasses().toList()
val methods = clazz[48].getDirectMethods().toList()

methods[0]


>>> dexFile.readUshort(4748448)
>>> dexFile.readSmallUint(4748448 + 8)

val fileLength = initialFile.length()
clazzes.indexOfFirst({
    c -> 
        c.getDirectMethods().indexOfFirst({
            m ->
                val offset = m.getImplementation()!!.codeOffset + 8 //DEBUG_INFO_OFFSET
                val debugOffset = dexFile.readInt(offset)
                debugOffset < 0 || debugOffset >= fileLength}) > 0 })

val clazz = clazzes[1874]
val methods = clazz.getDirectMethods().toList()
val mIdx = methods.indexOfFirst({
            m ->
                val offset = m.getImplementation()!!.codeOffset + 8 //DEBUG_INFO_OFFSET
                val debugOffset = dexFile.readInt(offset)
                debugOffset < 0 || debugOffset >= fileLength})
val method = methods[mIdx]

val impl = method.getImplementation()

val instructions = impl.getInstructions().toList()