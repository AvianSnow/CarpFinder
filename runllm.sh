#!/usr/bin/env sh
# Starts the language model a tribe's leader asks for advice (see .agent/society.md). It listens on this machine
# only. Then turn it on in the game server, in its own terminal: cvar cf.society_llm true
# or from the client console as an admin: sudo cvar cf.society_llm true
#
# llama.cpp is expected in ~/llama.cpp/current (the unzipped release, with the CUDA runtime DLLs beside it on
# Windows) and the model in ~/llama.cpp/models. Set LLAMA_HOME, LLAMA_MODEL, LLAMA_PORT, LLAMA_THREADS or
# LLAMA_GPU_LAYERS to change that. A CUDA build puts the whole model on the GPU; a CPU build ignores that flag.
LLAMA_HOME="${LLAMA_HOME:-$HOME/llama.cpp}"
LLAMA_MODEL="${LLAMA_MODEL:-$LLAMA_HOME/models/Llama-3.2-3B-Instruct-Q4_K_M.gguf}"
LLAMA_PORT="${LLAMA_PORT:-8080}"
LLAMA_THREADS="${LLAMA_THREADS:-8}"
LLAMA_GPU_LAYERS="${LLAMA_GPU_LAYERS:-99}"

SERVER="$LLAMA_HOME/current/llama-server"
[ -x "$SERVER" ] || SERVER="$SERVER.exe"
if [ ! -x "$SERVER" ]; then
    echo "No llama-server in $LLAMA_HOME/current. Unzip a llama.cpp release there (see .agent/society.md)." >&2
    exit 1
fi
if [ ! -f "$LLAMA_MODEL" ]; then
    echo "No model at $LLAMA_MODEL." >&2
    exit 1
fi

# One question at a time and a small context: each question is about 400 tokens.
exec "$SERVER" \
    --model "$LLAMA_MODEL" \
    --host 127.0.0.1 \
    --port "$LLAMA_PORT" \
    --ctx-size 2048 \
    --threads "$LLAMA_THREADS" \
    --n-gpu-layers "$LLAMA_GPU_LAYERS" \
    --parallel 1 \
    "$@"
