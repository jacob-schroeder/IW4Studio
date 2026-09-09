/*
 * MapConverter's narrow command-line wrapper around the vendored MojoShader
 * parser. The bundled upstream license is in ThirdParty/MojoShader/LICENSE.txt.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "mojoshader.h"

static int vertex_input_name(const MOJOSHADER_attribute *attribute,
                             char *name, size_t capacity)
{
    const char *semantic = NULL;
    switch (attribute->usage) {
        case MOJOSHADER_USAGE_POSITION:
            if (attribute->index == 0) semantic = "vertex.position";
            break;
        case MOJOSHADER_USAGE_BLENDWEIGHT:
            if (attribute->index == 0) semantic = "vertex.weight";
            break;
        case MOJOSHADER_USAGE_NORMAL:
            if (attribute->index == 0) semantic = "vertex.normal";
            break;
        case MOJOSHADER_USAGE_COLOR:
            if (attribute->index == 0) semantic = "vertex.color.primary";
            if (attribute->index == 1) semantic = "vertex.color.secondary";
            break;
        case MOJOSHADER_USAGE_FOG:
            if (attribute->index == 0) semantic = "vertex.fogcoord";
            break;
        case MOJOSHADER_USAGE_TEXCOORD:
            if (attribute->index >= 0 && attribute->index < 8) {
                snprintf(name, capacity, "vertex.texcoord[%d]", attribute->index);
                return 1;
            }
            break;
        default:
            break;
    }
    if (semantic == NULL) return 0;
    snprintf(name, capacity, "%s", semantic);
    return 1;
}

/* MojoShader's generic attribute numbers are GL binding locations.  Restore
 * the parsed D3D semantics so cgcomp selects the matching RSX input registers. */
static char *vertex_assembly(const MOJOSHADER_parseData *result)
{
    size_t length = (size_t)result->output_len;
    size_t capacity = length + (size_t)result->attribute_count * 64 + 1;
    char *assembly = (char *)malloc(capacity);
    int index;
    if (assembly == NULL) return NULL;
    memcpy(assembly, result->output, length);
    assembly[length] = '\0';

    for (index = 0; index < result->attribute_count; index++) {
        const MOJOSHADER_attribute *attribute = &result->attributes[index];
        char prefix[96], semantic[48];
        char *declaration, *begin, *end;
        size_t old_length, new_length;
        int prefix_length;
        if (attribute->name == NULL || attribute->name[0] == '\0' ||
            !vertex_input_name(attribute, semantic, sizeof(semantic))) {
            fprintf(stderr, "unsupported vertex input semantic %d[%d]\n",
                    attribute->usage, attribute->index);
            goto fail;
        }
        prefix_length = snprintf(prefix, sizeof(prefix),
                                 "ATTRIB %s = vertex.attrib[", attribute->name);
        if (prefix_length < 0 || (size_t)prefix_length >= sizeof(prefix)) goto fail;
        declaration = strstr(assembly, prefix);
        if (declaration == NULL ||
            (declaration != assembly && declaration[-1] != '\n') ||
            strstr(declaration + prefix_length, prefix) != NULL) goto fail;
        end = declaration + prefix_length;
        if (*end < '0' || *end > '9') goto fail;
        while (*end >= '0' && *end <= '9') end++;
        if (strncmp(end, "];", 2) != 0 ||
            (end[2] != '\0' && end[2] != '\n' &&
             !(end[2] == '\r' && end[3] == '\n'))) goto fail;
        end++; /* Keep the semicolon and original line ending. */
        begin = declaration + prefix_length - strlen("vertex.attrib[");
        old_length = (size_t)(end - begin);
        new_length = strlen(semantic);
        if (length - old_length + new_length >= capacity) goto fail;
        memmove(begin + new_length, end, length - (size_t)(end - assembly) + 1);
        memcpy(begin, semantic, new_length);
        length = length - old_length + new_length;
    }
    /* Relative vertex inputs bypass ATTRIB aliases and need separate lowering. */
    if (strstr(assembly, "vertex.attrib") != NULL) goto fail;
    return assembly;

fail:
    fprintf(stderr, "cannot preserve vertex input semantics in translated assembly\n");
    free(assembly);
    return NULL;
}

int main(int argc, char **argv)
{
    const MOJOSHADER_parseData *result;
    FILE *input;
    FILE *output;
    unsigned char *bytes;
    char *vertex_output = NULL;
    const char *assembly;
    size_t assembly_length;
    long length;
    int index;

    if (argc != 4) {
        fprintf(stderr, "usage: %s <nv3|nv4> <input-cso> <output-assembly>\\n", argv[0]);
        return 2;
    }
    if (strcmp(argv[1], "nv3") != 0 && strcmp(argv[1], "nv4") != 0) {
        fprintf(stderr, "unsupported MojoShader profile '%s'\\n", argv[1]);
        return 2;
    }
    input = fopen(argv[2], "rb");
    if (input == NULL) {
        fprintf(stderr, "cannot open '%s'\\n", argv[2]);
        return 1;
    }
    if (fseek(input, 0, SEEK_END) != 0 || (length = ftell(input)) <= 0 ||
        fseek(input, 0, SEEK_SET) != 0) {
        fprintf(stderr, "cannot size '%s'\\n", argv[2]);
        fclose(input);
        return 1;
    }
    bytes = (unsigned char *)malloc((size_t)length);
    if (bytes == NULL || fread(bytes, 1, (size_t)length, input) != (size_t)length) {
        fprintf(stderr, "cannot read '%s'\\n", argv[2]);
        free(bytes);
        fclose(input);
        return 1;
    }
    fclose(input);

    result = MOJOSHADER_parse(argv[1], NULL, bytes, (unsigned int)length,
                              NULL, 0, NULL, 0, NULL, NULL, NULL);
    free(bytes);
    if (result->error_count != 0 || result->output == NULL) {
        for (index = 0; index < result->error_count; index++)
            fprintf(stderr, "%s\\n", result->errors[index].error);
        MOJOSHADER_freeParseData(result);
        return 1;
    }
    if (result->shader_type == MOJOSHADER_TYPE_VERTEX) {
        vertex_output = vertex_assembly(result);
        if (vertex_output == NULL) {
            MOJOSHADER_freeParseData(result);
            return 1;
        }
    }
    assembly = vertex_output != NULL ? vertex_output : result->output;
    assembly_length = vertex_output != NULL ? strlen(vertex_output) : (size_t)result->output_len;
    output = fopen(argv[3], "wb");
    if (output == NULL || fwrite(assembly, 1, assembly_length, output) != assembly_length) {
        fprintf(stderr, "cannot write '%s'\\n", argv[3]);
        if (output != NULL) fclose(output);
        free(vertex_output);
        MOJOSHADER_freeParseData(result);
        return 1;
    }
    fclose(output);
    free(vertex_output);
    MOJOSHADER_freeParseData(result);
    return 0;
}
