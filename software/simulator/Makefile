# PersonaSticker PCシミュレーター
# 使い方: make && ./persona_sim  →  ブラウザで http://localhost:8080
CXX ?= g++
CXXFLAGS ?= -std=c++17 -O2 -Wall -Wextra
CORE_DIR = ../firmware/lib/emotion_core/src
INCLUDES = -I$(CORE_DIR) -Isrc

CORE_SRCS = $(CORE_DIR)/persona_features.cpp \
            $(CORE_DIR)/emotion_engine.cpp \
            $(CORE_DIR)/face_renderer.cpp
SIM_SRCS = src/main.cpp src/http_server.cpp src/json_util.cpp src/sim_world.cpp

persona_sim: $(SIM_SRCS) $(CORE_SRCS) $(wildcard src/*.h) $(wildcard $(CORE_DIR)/*.h)
	$(CXX) $(CXXFLAGS) $(INCLUDES) $(SIM_SRCS) $(CORE_SRCS) -o $@ -lpthread

.PHONY: clean run
clean:
	rm -f persona_sim

run: persona_sim
	./persona_sim
