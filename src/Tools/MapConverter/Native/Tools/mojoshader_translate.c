/*
 * MapConverter's narrow command-line wrapper around the vendored MojoShader
 * parser. The bundled upstream license is in ThirdParty/MojoShader/LICENSE.txt.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "mojoshader.h"

int main(int argc, char **argv)
{
    const MOJOSHADER_parseData *result;
    FILE *input;
    FILE *output;
    unsigned char *bytes;
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
    output = fopen(argv[3], "wb");
    if (output == NULL || fwrite(result->output, 1, result->output_len, output) != result->output_len) {
        fprintf(stderr, "cannot write '%s'\\n", argv[3]);
        if (output != NULL) fclose(output);
        MOJOSHADER_freeParseData(result);
        return 1;
    }
    fclose(output);
    MOJOSHADER_freeParseData(result);
    return 0;
}
