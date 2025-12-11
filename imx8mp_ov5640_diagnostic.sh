#!/bin/bash
#===============================================================================
# IMX8M Plus + OV5640 MIPI Camera Diagnostic Script
# 
# This script captures comprehensive diagnostic information to help identify
# MIPI timing/synchronization issues with the OV5640 camera.
#
# Usage: 
#   ./imx8mp_ov5640_diagnostic.sh [--capture] [--continuous N]
#
# Options:
#   --capture       Attempt a capture and log results
#   --continuous N  Run N capture attempts and log statistics
#   (no args)       Just collect system/driver state information
#===============================================================================

set -e

# Configuration
VIDEO_DEVICE="${VIDEO_DEVICE:-/dev/video0}"
MEDIA_DEVICE="${MEDIA_DEVICE:-/dev/media0}"
LOG_DIR="/tmp/mipi_diag_$(date +%Y%m%d_%H%M%S)"
CAPTURE_COUNT=1
CAPTURE_WIDTH=1920
CAPTURE_HEIGHT=1080
CAPTURE_FORMAT="YUYV"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

#-------------------------------------------------------------------------------
# Helper Functions
#-------------------------------------------------------------------------------

log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[OK]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

section_header() {
    echo ""
    echo "==============================================================================="
    echo " $1"
    echo "==============================================================================="
}

check_root() {
    if [ "$EUID" -ne 0 ]; then
        log_warning "Running without root - some diagnostics may be limited"
        log_warning "Consider running with: sudo $0 $@"
    fi
}

ensure_log_dir() {
    mkdir -p "$LOG_DIR"
    log_info "Logs will be saved to: $LOG_DIR"
}

#-------------------------------------------------------------------------------
# System Information
#-------------------------------------------------------------------------------

collect_system_info() {
    section_header "System Information"
    
    local sysinfo_file="$LOG_DIR/01_system_info.log"
    
    {
        echo "=== Kernel Version ==="
        uname -a
        
        echo ""
        echo "=== Device Tree Model ==="
        cat /proc/device-tree/model 2>/dev/null || echo "N/A"
        
        echo ""
        echo "=== CPU Info ==="
        cat /proc/cpuinfo | head -20
        
        echo ""
        echo "=== Memory Info ==="
        free -h
        
        echo ""
        echo "=== Kernel Command Line ==="
        cat /proc/cmdline
        
    } > "$sysinfo_file" 2>&1
    
    log_success "System info saved to $sysinfo_file"
}

#-------------------------------------------------------------------------------
# Kernel Module Information
#-------------------------------------------------------------------------------

collect_module_info() {
    section_header "Kernel Modules (Camera Related)"
    
    local modinfo_file="$LOG_DIR/02_modules.log"
    
    {
        echo "=== Loaded Modules (camera/mipi related) ==="
        lsmod | grep -iE "ov5640|imx8|mipi|csi|isp|v4l2|videobuf" || echo "No matching modules found"
        
        echo ""
        echo "=== OV5640 Module Info ==="
        modinfo ov5640 2>/dev/null || echo "ov5640 module info not available"
        
        echo ""
        echo "=== MIPI CSI2 Module Info ==="
        modinfo imx8-mipi-csi2 2>/dev/null || \
        modinfo mxc-mipi-csi2 2>/dev/null || \
        modinfo imx8mq-mipi-csi2 2>/dev/null || \
        echo "MIPI CSI2 module info not available"
        
        echo ""
        echo "=== ISI Module Info ==="
        modinfo imx8-isi 2>/dev/null || \
        modinfo mxc-isi 2>/dev/null || \
        echo "ISI module info not available"
        
    } > "$modinfo_file" 2>&1
    
    log_success "Module info saved to $modinfo_file"
}

#-------------------------------------------------------------------------------
# Video Device Information
#-------------------------------------------------------------------------------

collect_video_device_info() {
    section_header "Video Device Information"
    
    local video_file="$LOG_DIR/03_video_devices.log"
    
    {
        echo "=== Video Devices ==="
        v4l2-ctl --list-devices 2>/dev/null || echo "v4l2-ctl not available or no devices"
        
        echo ""
        echo "=== $VIDEO_DEVICE Capabilities ==="
        v4l2-ctl -d "$VIDEO_DEVICE" --all 2>/dev/null || echo "Cannot query $VIDEO_DEVICE"
        
        echo ""
        echo "=== $VIDEO_DEVICE Supported Formats ==="
        v4l2-ctl -d "$VIDEO_DEVICE" --list-formats-ext 2>/dev/null || echo "Cannot list formats"
        
        echo ""
        echo "=== $VIDEO_DEVICE Controls ==="
        v4l2-ctl -d "$VIDEO_DEVICE" --list-ctrls-menus 2>/dev/null || echo "Cannot list controls"
        
    } > "$video_file" 2>&1
    
    log_success "Video device info saved to $video_file"
}

#-------------------------------------------------------------------------------
# Media Controller Pipeline
#-------------------------------------------------------------------------------

collect_media_pipeline_info() {
    section_header "Media Controller Pipeline"
    
    local media_file="$LOG_DIR/04_media_pipeline.log"
    
    {
        echo "=== Media Devices ==="
        ls -la /dev/media* 2>/dev/null || echo "No media devices found"
        
        echo ""
        echo "=== Media Pipeline Topology ==="
        media-ctl -d "$MEDIA_DEVICE" -p 2>/dev/null || echo "Cannot query media device"
        
        echo ""
        echo "=== Entity Links ==="
        media-ctl -d "$MEDIA_DEVICE" --print-dot 2>/dev/null || echo "Cannot print topology"
        
    } > "$media_file" 2>&1
    
    log_success "Media pipeline info saved to $media_file"
}

#-------------------------------------------------------------------------------
# I2C Bus Information
#-------------------------------------------------------------------------------

collect_i2c_info() {
    section_header "I2C Bus Information (OV5640)"
    
    local i2c_file="$LOG_DIR/05_i2c_info.log"
    
    {
        echo "=== I2C Buses ==="
        ls -la /dev/i2c-* 2>/dev/null || echo "No I2C buses found"
        
        echo ""
        echo "=== Scanning for OV5640 (address 0x3c) ==="
        for bus in /dev/i2c-*; do
            bus_num=$(echo "$bus" | grep -o '[0-9]*$')
            echo "--- Bus $bus_num ---"
            i2cdetect -y "$bus_num" 2>/dev/null || echo "Cannot scan bus $bus_num"
        done
        
        echo ""
        echo "=== Device Tree Camera Nodes ==="
        find /proc/device-tree -name "*ov5640*" -o -name "*camera*" 2>/dev/null | while read node; do
            echo "Node: $node"
            if [ -f "$node/status" ]; then
                echo "  Status: $(cat $node/status 2>/dev/null)"
            fi
            if [ -f "$node/reg" ]; then
                echo "  Reg: $(hexdump -C $node/reg 2>/dev/null)"
            fi
        done
        
    } > "$i2c_file" 2>&1
    
    log_success "I2C info saved to $i2c_file"
}

#-------------------------------------------------------------------------------
# DebugFS Information (MIPI CSI-2 Status)
#-------------------------------------------------------------------------------

collect_debugfs_info() {
    section_header "DebugFS (MIPI CSI-2 Status)"
    
    local debug_file="$LOG_DIR/06_debugfs.log"
    
    {
        echo "=== Mounting debugfs if needed ==="
        if ! mountpoint -q /sys/kernel/debug; then
            mount -t debugfs none /sys/kernel/debug 2>/dev/null || echo "Cannot mount debugfs"
        fi
        
        echo ""
        echo "=== MIPI CSI-2 Debug Info ==="
        
        # Try various possible paths for MIPI debug info
        for path in /sys/kernel/debug/*mipi* /sys/kernel/debug/*csi* /sys/kernel/debug/imx8* /sys/kernel/debug/mxc*; do
            if [ -d "$path" ] 2>/dev/null; then
                echo "--- $path ---"
                find "$path" -type f 2>/dev/null | while read f; do
                    echo "File: $f"
                    cat "$f" 2>/dev/null || echo "(cannot read)"
                    echo ""
                done
            fi
        done
        
        echo ""
        echo "=== Clock Information ==="
        if [ -d /sys/kernel/debug/clk ]; then
            echo "Camera-related clocks:"
            find /sys/kernel/debug/clk -name "*csi*" -o -name "*mipi*" -o -name "*camera*" -o -name "*clko*" 2>/dev/null | while read clk; do
                if [ -f "$clk/clk_rate" ]; then
                    echo "$clk: $(cat $clk/clk_rate 2>/dev/null) Hz"
                fi
            done
        fi
        
        echo ""
        echo "=== regulator Information ==="
        if [ -d /sys/class/regulator ]; then
            for reg in /sys/class/regulator/regulator.*; do
                name=$(cat "$reg/name" 2>/dev/null)
                if echo "$name" | grep -qiE "cam|ov5640|dovdd|avdd|dvdd"; then
                    echo "Regulator: $name"
                    echo "  State: $(cat $reg/state 2>/dev/null)"
                    echo "  Voltage: $(cat $reg/microvolts 2>/dev/null) uV"
                fi
            done
        fi
        
    } > "$debug_file" 2>&1
    
    log_success "DebugFS info saved to $debug_file"
}

#-------------------------------------------------------------------------------
# Kernel Messages (dmesg)
#-------------------------------------------------------------------------------

collect_dmesg() {
    section_header "Kernel Messages"
    
    local dmesg_file="$LOG_DIR/07_dmesg.log"
    local dmesg_filtered="$LOG_DIR/07_dmesg_camera.log"
    
    {
        echo "=== Full dmesg ==="
        dmesg
    } > "$dmesg_file" 2>&1
    
    {
        echo "=== Camera/MIPI Related Messages ==="
        dmesg | grep -iE "ov5640|mipi|csi|isi|camera|v4l2|videobuf|sync|timeout|error|fail" || echo "No matching messages"
    } > "$dmesg_filtered" 2>&1
    
    log_success "dmesg saved to $dmesg_file"
    log_success "Filtered dmesg saved to $dmesg_filtered"
}

#-------------------------------------------------------------------------------
# GPIO and Pin Status
#-------------------------------------------------------------------------------

collect_gpio_info() {
    section_header "GPIO Information (Reset/Power pins)"
    
    local gpio_file="$LOG_DIR/08_gpio.log"
    
    {
        echo "=== GPIO Chip Info ==="
        ls -la /sys/class/gpio/ 2>/dev/null
        
        echo ""
        echo "=== Exported GPIOs ==="
        for gpio in /sys/class/gpio/gpio*; do
            if [ -d "$gpio" ]; then
                echo "GPIO: $(basename $gpio)"
                echo "  Direction: $(cat $gpio/direction 2>/dev/null)"
                echo "  Value: $(cat $gpio/value 2>/dev/null)"
            fi
        done
        
        echo ""
        echo "=== GPIO Labels (Camera Related) ==="
        cat /sys/kernel/debug/gpio 2>/dev/null | grep -iE "reset|pwdn|power|standby|cam" || echo "No camera GPIOs found"
        
    } > "$gpio_file" 2>&1
    
    log_success "GPIO info saved to $gpio_file"
}

#-------------------------------------------------------------------------------
# Capture Test
#-------------------------------------------------------------------------------

perform_capture_test() {
    section_header "Capture Test"
    
    local capture_file="$LOG_DIR/09_capture_test.log"
    local before_dmesg="$LOG_DIR/09_before_capture_dmesg.log"
    local after_dmesg="$LOG_DIR/09_after_capture_dmesg.log"
    local raw_file="$LOG_DIR/capture_test.raw"
    
    log_info "Capturing dmesg before test..."
    dmesg > "$before_dmesg" 2>&1
    
    log_info "Attempting capture from $VIDEO_DEVICE..."
    
    {
        echo "=== Capture Attempt ==="
        echo "Device: $VIDEO_DEVICE"
        echo "Resolution: ${CAPTURE_WIDTH}x${CAPTURE_HEIGHT}"
        echo "Format: $CAPTURE_FORMAT"
        echo "Timestamp: $(date)"
        echo ""
        
        # Set format
        echo "Setting format..."
        v4l2-ctl -d "$VIDEO_DEVICE" \
            --set-fmt-video=width=$CAPTURE_WIDTH,height=$CAPTURE_HEIGHT,pixelformat=$CAPTURE_FORMAT 2>&1 || true
        
        echo ""
        echo "Current format:"
        v4l2-ctl -d "$VIDEO_DEVICE" --get-fmt-video 2>&1 || true
        
        echo ""
        echo "Starting stream capture..."
        
        # Capture with timeout
        timeout 10 v4l2-ctl -d "$VIDEO_DEVICE" \
            --stream-mmap \
            --stream-count=$CAPTURE_COUNT \
            --stream-to="$raw_file" 2>&1
        
        CAPTURE_RESULT=$?
        
        echo ""
        echo "=== Capture Result ==="
        echo "Exit code: $CAPTURE_RESULT"
        
        if [ $CAPTURE_RESULT -eq 0 ]; then
            echo "Status: SUCCESS"
            if [ -f "$raw_file" ]; then
                echo "Output file size: $(stat -c%s "$raw_file") bytes"
                echo "Expected min size: $((CAPTURE_WIDTH * CAPTURE_HEIGHT * 2)) bytes (YUYV)"
            fi
        else
            echo "Status: FAILED"
        fi
        
    } > "$capture_file" 2>&1
    
    log_info "Capturing dmesg after test..."
    dmesg > "$after_dmesg" 2>&1
    
    # Create diff of dmesg
    local dmesg_diff="$LOG_DIR/09_dmesg_diff.log"
    {
        echo "=== New kernel messages during capture ==="
        diff "$before_dmesg" "$after_dmesg" | grep "^>" | sed 's/^> //'
    } > "$dmesg_diff" 2>&1
    
    log_success "Capture test results saved to $capture_file"
    log_success "dmesg diff saved to $dmesg_diff"
    
    # Display result
    if grep -q "Status: SUCCESS" "$capture_file"; then
        log_success "Capture successful!"
    else
        log_error "Capture FAILED - check logs for details"
        
        # Show relevant errors
        echo ""
        log_info "New kernel messages during capture:"
        cat "$dmesg_diff" | grep -iE "error|fail|timeout|sync" || echo "  (none matching error patterns)"
    fi
}

#-------------------------------------------------------------------------------
# Continuous Capture Test
#-------------------------------------------------------------------------------

perform_continuous_test() {
    local num_attempts=$1
    section_header "Continuous Capture Test ($num_attempts attempts)"
    
    local stats_file="$LOG_DIR/10_continuous_stats.log"
    local success_count=0
    local fail_count=0
    
    log_info "Running $num_attempts capture attempts..."
    echo ""
    
    {
        echo "=== Continuous Capture Test ==="
        echo "Total attempts: $num_attempts"
        echo "Start time: $(date)"
        echo ""
        
        for i in $(seq 1 $num_attempts); do
            echo "--- Attempt $i/$num_attempts ---"
            echo "Timestamp: $(date)"
            
            local raw_file="$LOG_DIR/continuous_$i.raw"
            
            # Quick capture attempt
            timeout 5 v4l2-ctl -d "$VIDEO_DEVICE" \
                --stream-mmap \
                --stream-count=1 \
                --stream-to="$raw_file" 2>&1
            
            local result=$?
            
            if [ $result -eq 0 ] && [ -f "$raw_file" ] && [ $(stat -c%s "$raw_file") -gt 0 ]; then
                echo "Result: SUCCESS"
                ((success_count++))
                printf "${GREEN}.${NC}"
                rm -f "$raw_file"  # Clean up successful captures
            else
                echo "Result: FAILED (exit code: $result)"
                ((fail_count++))
                printf "${RED}X${NC}"
                
                # Keep failed capture logs
                dmesg | tail -20 >> "$LOG_DIR/10_fail_$i.log"
            fi
            
            echo ""
            sleep 0.5  # Small delay between attempts
        done
        
        echo ""
        echo "=== Summary ==="
        echo "Success: $success_count/$num_attempts"
        echo "Failed: $fail_count/$num_attempts"
        echo "Success rate: $((success_count * 100 / num_attempts))%"
        echo "End time: $(date)"
        
    } > "$stats_file" 2>&1
    
    echo ""
    log_info "Results: Success=$success_count, Failed=$fail_count"
    log_success "Statistics saved to $stats_file"
    
    if [ $fail_count -gt 0 ]; then
        log_warning "Failed attempt logs saved as 10_fail_*.log"
    fi
}

#-------------------------------------------------------------------------------
# Analysis and Recommendations
#-------------------------------------------------------------------------------

analyze_results() {
    section_header "Analysis"
    
    local analysis_file="$LOG_DIR/99_analysis.log"
    
    {
        echo "=== Automated Analysis ==="
        echo "Timestamp: $(date)"
        echo ""
        
        # Check for common MIPI sync errors
        echo "=== Checking for MIPI Sync Errors ==="
        if grep -rqiE "sync error|sot error|lane sync" "$LOG_DIR"/*.log 2>/dev/null; then
            echo "[!] MIPI SYNC ERRORS DETECTED"
            echo "    This indicates timing issues between the camera and the i.MX8M Plus."
            echo "    Possible solutions:"
            echo "    - Adjust MIPI lane configuration"
            echo "    - Check clock frequencies"
            echo "    - Add delays before streaming"
            echo ""
            grep -riE "sync error|sot error|lane sync" "$LOG_DIR"/*.log
        else
            echo "[OK] No MIPI sync errors found"
        fi
        echo ""
        
        # Check for timeout errors
        echo "=== Checking for Timeout Errors ==="
        if grep -rqiE "timeout|timed out" "$LOG_DIR"/*.log 2>/dev/null; then
            echo "[!] TIMEOUT ERRORS DETECTED"
            echo "    This could indicate:"
            echo "    - Camera not responding to commands"
            echo "    - I2C communication issues"
            echo "    - Power sequencing problems"
            echo ""
            grep -riE "timeout|timed out" "$LOG_DIR"/*.log
        else
            echo "[OK] No timeout errors found"
        fi
        echo ""
        
        # Check for I2C errors
        echo "=== Checking for I2C Errors ==="
        if grep -rqiE "i2c.*error|i2c.*fail|nack" "$LOG_DIR"/*.log 2>/dev/null; then
            echo "[!] I2C ERRORS DETECTED"
            echo "    Camera configuration may have failed."
            echo "    Check I2C bus connections and address (0x3c for OV5640)."
            echo ""
            grep -riE "i2c.*error|i2c.*fail|nack" "$LOG_DIR"/*.log
        else
            echo "[OK] No I2C errors found"
        fi
        echo ""
        
        # Check for CRC/ECC errors
        echo "=== Checking for Data Integrity Errors ==="
        if grep -rqiE "crc error|ecc error|checksum" "$LOG_DIR"/*.log 2>/dev/null; then
            echo "[!] DATA INTEGRITY ERRORS DETECTED"
            echo "    MIPI data transmission is corrupted."
            echo "    Check:"
            echo "    - MIPI cable/connections"
            echo "    - Lane configuration"
            echo "    - Signal integrity"
            echo ""
            grep -riE "crc error|ecc error|checksum" "$LOG_DIR"/*.log
        else
            echo "[OK] No data integrity errors found"
        fi
        echo ""
        
        # Check for FIFO errors
        echo "=== Checking for Buffer/FIFO Errors ==="
        if grep -rqiE "fifo|overflow|underflow|overrun" "$LOG_DIR"/*.log 2>/dev/null; then
            echo "[!] BUFFER ERRORS DETECTED"
            echo "    This could indicate bandwidth or timing issues."
            echo ""
            grep -riE "fifo|overflow|underflow|overrun" "$LOG_DIR"/*.log
        else
            echo "[OK] No buffer errors found"
        fi
        echo ""
        
        echo "=== Recommendations ==="
        echo ""
        echo "Based on the analysis, here are general recommendations:"
        echo ""
        echo "1. If MIPI sync errors: Review device tree MIPI lane and clock settings"
        echo "2. If timeout errors: Add delays after camera power-up before streaming"
        echo "3. If I2C errors: Check camera connections and I2C pull-ups"
        echo "4. If data errors: Check for proper termination and signal integrity"
        echo ""
        echo "For detailed analysis, review the individual log files in:"
        echo "$LOG_DIR/"
        
    } > "$analysis_file" 2>&1
    
    log_success "Analysis saved to $analysis_file"
    
    # Print summary to console
    echo ""
    log_info "Quick Summary:"
    grep -E "^\[!\]|\[OK\]" "$analysis_file" | head -10
}

#-------------------------------------------------------------------------------
# Create Archive
#-------------------------------------------------------------------------------

create_archive() {
    section_header "Creating Archive"
    
    local archive_name="mipi_diag_$(date +%Y%m%d_%H%M%S).tar.gz"
    local archive_path="/tmp/$archive_name"
    
    tar -czf "$archive_path" -C "$(dirname $LOG_DIR)" "$(basename $LOG_DIR)"
    
    log_success "Archive created: $archive_path"
    log_info "Transfer this file for offline analysis"
}

#-------------------------------------------------------------------------------
# Main
#-------------------------------------------------------------------------------

main() {
    local do_capture=0
    local continuous_count=0
    
    # Parse arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            --capture)
                do_capture=1
                shift
                ;;
            --continuous)
                continuous_count="$2"
                shift 2
                ;;
            --device)
                VIDEO_DEVICE="$2"
                shift 2
                ;;
            --help|-h)
                echo "Usage: $0 [OPTIONS]"
                echo ""
                echo "Options:"
                echo "  --capture         Perform single capture test"
                echo "  --continuous N    Perform N capture tests"
                echo "  --device DEV      Use specified video device (default: /dev/video0)"
                echo "  --help            Show this help"
                exit 0
                ;;
            *)
                log_error "Unknown option: $1"
                exit 1
                ;;
        esac
    done
    
    echo "==============================================================================="
    echo " IMX8M Plus + OV5640 MIPI Camera Diagnostic Script"
    echo " $(date)"
    echo "==============================================================================="
    
    check_root
    ensure_log_dir
    
    # Always collect system state
    collect_system_info
    collect_module_info
    collect_video_device_info
    collect_media_pipeline_info
    collect_i2c_info
    collect_debugfs_info
    collect_dmesg
    collect_gpio_info
    
    # Optional capture tests
    if [ $do_capture -eq 1 ]; then
        perform_capture_test
    fi
    
    if [ $continuous_count -gt 0 ]; then
        perform_continuous_test $continuous_count
    fi
    
    # Always analyze and create archive
    analyze_results
    create_archive
    
    section_header "Complete"
    log_info "All diagnostics saved to: $LOG_DIR"
    log_info "Archive: /tmp/mipi_diag_*.tar.gz"
    echo ""
}

main "$@"
