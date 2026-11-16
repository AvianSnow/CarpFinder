#!/usr/bin/env sh
# Starts the language model a tribe's leader asks for advice (see .agent/society.md). It listens on this machine
# only. Then turn it on in the game server: cf.society_llm true
#
# llama.cpp is expected in ~/llama.cpp/current and the model in ~/llama.cpp/models. Set LLAMA_HOME, LLAMA_MODEL,
# LLAMA_PORT or LLAMA_THREADS to change that.
LLAMA_HOME="${LLAMA_HOME:-$HOME/llama.cpp}"
LLAMA_MODEL="${LLAMA_MODEL:-$LLAMA_HOME/models/Llama-3.2-3B-Instruct-Q4_K_M.gguf}"
LLAMA_PORT="${LLAMA_PORT:-8080}"
LLAMA_THREADS="${LLAMA_THREADS:-8}"

# One question at a time and a small context: each question is about 400 tokens.
exec "$LLAMA_HOME/current/llama-server" \
    --model "$LLAMA_MODEL" \
    --host 127.0.0.1 \
    --port "$LLAMA_PORT" \
    --ctx-size 2048 \
    --threads "$LLAMA_THREADS" \
    --parallel 1 \
    "$@"
