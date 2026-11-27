/*
 * MapConverter's in-memory interface to the vendored MojoShader
 * parser. The bundled upstream license is in ThirdParty/MojoShader/LICENSE.txt.
 */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "mojoshader.h"

#ifdef _WIN32
#define IW4_EXPORT __declspec(dllexport)
#else
#define IW4_EXPORT __attribute__((visibility("default")))
#endif

static char *copy_text(const char *text, size_t length)
{
    char *copy = (char *)malloc(length + 1);
    if (copy != NULL) {
        memcpy(copy, text, length);
        copy[length] = '\0';
    }
    return copy;
}

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
static char *vertex_assembly(const MOJOSHADER_parseData *result, char **error)
{
    size_t length = (size_t)result->output_len;
    if ((size_t)result->attribute_count > (SIZE_MAX - length - 1) / 64) return NULL;
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
            char message[96];
            snprintf(message, sizeof(message), "Unsupported vertex input semantic %d[%d].",
                     attribute->usage, attribute->index);
            *error = copy_text(message, strlen(message));
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
    if (*error == NULL) {
        const char message[] = "Cannot preserve vertex input semantics in translated assembly.";
        *error = copy_text(message, sizeof(message) - 1);
    }
    free(assembly);
    return NULL;
}

static char *parse_errors(const MOJOSHADER_parseData *result)
{
    size_t length = 0;
    int i;
    for (i = 0; i < result->error_count; i++) {
        size_t count = strlen(result->errors[i].error);
        if (count >= SIZE_MAX - length) return NULL;
        length += count + 1;
    }
    if (length == 0) return NULL;
    char *message = (char *)malloc(length);
    if (message == NULL) return NULL;
    char *cursor = message;
    for (i = 0; i < result->error_count; i++) {
        const char *part = result->errors[i].error;
        size_t count = strlen(part);
        memcpy(cursor, part, count);
        cursor += count;
        *cursor++ = i + 1 == result->error_count ? '\0' : '\n';
    }
    return message;
}

IW4_EXPORT int iw4_translate_d3d9(const char *profile, const unsigned char *bytes,
                                  unsigned int length, char **output, char **error)
{
    *output = NULL;
    *error = NULL;
    const MOJOSHADER_parseData *result = MOJOSHADER_parse(profile, NULL, bytes, length,
                                                          NULL, 0, NULL, 0, NULL, NULL, NULL);
    if (result->error_count != 0 || result->output == NULL)
        *error = parse_errors(result);
    else if (result->shader_type == MOJOSHADER_TYPE_VERTEX)
        *output = vertex_assembly(result, error);
    else
        *output = copy_text(result->output, (size_t)result->output_len);
    MOJOSHADER_freeParseData(result);
    return *output == NULL;
}

IW4_EXPORT void iw4_shader_free(void *buffer)
{
    free(buffer);
}
