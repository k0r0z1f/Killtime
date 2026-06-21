import sys
import json
import os
import glob
import math
from PyQt6.QtWidgets import (QApplication, QMainWindow, QGraphicsScene, QGraphicsView, 
                             QVBoxLayout, QHBoxLayout, QWidget, QPushButton, QDialog, 
                             QFormLayout, QLineEdit, QComboBox, QSpinBox, QDialogButtonBox,
                             QLabel, QFileDialog, QMenuBar, QStatusBar, QGraphicsItem, QMenu)
from PyQt6.QtGui import QPen, QColor, QBrush, QPainterPath, QFont, QPainter, QAction
from PyQt6.QtCore import Qt, QTimer, QLineF, QRectF

# --- CONFIGURATION ---
PIXELS_PER_YEAR = 100  # Scale: 100 pixels represents 1 year horizontally

def date_to_float(year, month, day):
    """Converts the Hybris Calendar (10 months, 30 days) to a float for the X-axis."""
    year_fraction = ((month - 1) * 30 + (day - 1)) / 300.0
    return year + year_fraction

def float_to_date(float_val):
    """Converts a float X-axis value back to Hybris year, month, and day."""
    clean_v = round(float_val * 300) / 300.0 
    year = int(math.floor(clean_v))
    rem = clean_v - year
    total_days = round(rem * 300)
    month = (total_days // 30) + 1
    day = (total_days % 30) + 1
    return year, month, day

class HybrisDateWidget(QWidget):
    def __init__(self, y=1788, m=1, d=1):
        super().__init__()
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        
        self.y_spin = QSpinBox()
        self.y_spin.setRange(-5000000, 5000000) 
        self.y_spin.setValue(y)
        
        self.m_spin = QSpinBox()
        self.m_spin.setRange(1, 10)
        self.m_spin.setValue(m)
        
        self.d_spin = QSpinBox()
        self.d_spin.setRange(1, 30)
        self.d_spin.setValue(d)
        
        layout.addWidget(QLabel("Y:"))
        layout.addWidget(self.y_spin)
        layout.addWidget(QLabel("M:"))
        layout.addWidget(self.m_spin)
        layout.addWidget(QLabel("D:"))
        layout.addWidget(self.d_spin)

    def get_float_year(self):
        return date_to_float(self.y_spin.value(), self.m_spin.value(), self.d_spin.value())

    def get_date_str(self):
        return f"{self.y_spin.value():04d}-{self.m_spin.value():02d}-{self.d_spin.value():02d}"


class AddEventDialog(QDialog):
    def __init__(self, timelines, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Add Fixed Event (|)")
        self.layout = QFormLayout(self)

        self.line_combo = QComboBox()
        self.line_combo.addItems(timelines.keys())
        self.layout.addRow("Target Timeline:", self.line_combo)

        self.date_input = HybrisDateWidget(1788, 1, 1)
        self.layout.addRow("Hybris Date:", self.date_input)

        self.name_input = QLineEdit()
        self.layout.addRow("Event Name:", self.name_input)

        self.buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        self.buttons.accepted.connect(self.accept)
        self.buttons.rejected.connect(self.reject)
        self.layout.addWidget(self.buttons)


class AddBranchDialog(QDialog):
    def __init__(self, timelines, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Create Timeline Branch (o)")
        self.layout = QFormLayout(self)

        self.parent_line_combo = QComboBox()
        self.parent_line_combo.addItems(timelines.keys())
        self.layout.addRow("Parent Timeline:", self.parent_line_combo)

        self.departure_input = HybrisDateWidget(1788, 1, 1)
        self.layout.addRow("Departure Date (Trigger):", self.departure_input)

        self.arrival_input = HybrisDateWidget(1788, 1, 1)
        self.layout.addRow("Arrival Date (Branch Start):", self.arrival_input)

        self.trigger_name_input = QLineEdit()
        self.layout.addRow("Trigger Event Name:", self.trigger_name_input)

        self.branch_name_input = QLineEdit()
        self.layout.addRow("New Branch Name:", self.branch_name_input)

        self.buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        self.buttons.accepted.connect(self.accept)
        self.buttons.rejected.connect(self.reject)
        self.layout.addWidget(self.buttons)

class EditEventDialog(QDialog):
    def __init__(self, event_data, timelines, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Edit Event")
        self.layout = QFormLayout(self)

        self.is_trigger = event_data["type"] == "trigger"
        self.target_branch = event_data.get("target_branch")

        self.line_combo = QComboBox()
        self.line_combo.addItems(timelines.keys())
        self.line_combo.setCurrentText(event_data["line_name"])
        if self.is_trigger:
            self.line_combo.setEnabled(False) # Prevent breaking branches by switching lines
        self.layout.addRow("Target Timeline:", self.line_combo)

        # Parse existing date
        try:
            y, m, d = map(int, event_data["date_str"].split("-"))
        except:
            y, m, d = 1788, 1, 1
            
        self.date_input = HybrisDateWidget(y, m, d)
        
        if self.is_trigger:
            self.layout.addRow("Departure Date (Trigger):", self.date_input)
            
            ay, am, ad = y, m, d
            if self.target_branch and self.target_branch in timelines:
                ay, am, ad = float_to_date(timelines[self.target_branch]["start_val"])
            self.arrival_input = HybrisDateWidget(ay, am, ad)
            self.layout.addRow("Arrival Date (Branch):", self.arrival_input)
        else:
            self.layout.addRow("Hybris Date:", self.date_input)

        self.name_input = QLineEdit(event_data["name"])
        self.layout.addRow("Event Name:", self.name_input)

        self.buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        self.buttons.accepted.connect(self.accept)
        self.buttons.rejected.connect(self.reject)
        self.layout.addWidget(self.buttons)

class InfiniteDashLineItem(QGraphicsItem):
    def __init__(self, x1, x2, y, pen):
        super().__init__()
        self.x1 = min(x1, x2)
        self.x2 = max(x1, x2)
        self.y_pos = y
        self.pen = pen
        self.pattern_length = sum(pen.dashPattern())
        if self.pattern_length == 0:
            self.pattern_length = 1.0

    def boundingRect(self):
        # Generous bounding rect so it stays active during panning
        return QRectF(self.x1, self.y_pos - 50, self.x2 - self.x1, 100)

    def paint(self, painter, option, widget=None):
        device = painter.device()
        if not device: return

        # 1) Since FullViewportUpdate makes exposedRect cover the entire huge item, 
        # we bypass it and calculate the exact visible screen bounds via the viewport device.
        view_rect = QRectF(0, 0, device.width(), device.height())
        inv_transform, invertible = painter.transform().inverted()
        if not invertible: return
        
        logical_rect = inv_transform.mapRect(view_rect)
        
        # 2) Calculate draw segment clamping to item bounds and visible bounds
        scale_x = abs(painter.transform().m11())
        if scale_x == 0: scale_x = 1.0
        
        pad = 200 / scale_x # Pad dynamically by 200 screen pixels
        
        draw_x1 = max(self.x1, logical_rect.left() - pad)
        draw_x2 = min(self.x2, logical_rect.right() + pad)

        if draw_x1 > draw_x2:
            return

        # 3) Determine dash offset so the pattern remains perfectly anchored globally
        pixel_dist = (draw_x1 - self.x1) * scale_x
        pen_width = self.pen.widthF()
        if pen_width == 0: pen_width = 1.0 
            
        offset_units = (pixel_dist / pen_width) % self.pattern_length
        
        draw_pen = QPen(self.pen)
        draw_pen.setDashOffset(offset_units)
        
        painter.setPen(draw_pen)
        painter.drawLine(QLineF(draw_x1, self.y_pos, draw_x2, self.y_pos))


class RiverView(QGraphicsView):
    def __init__(self, scene, main_window):
        super().__init__(scene)
        self.main_window = main_window
        self.setRenderHint(QPainter.RenderHint.Antialiasing)
        self.setDragMode(QGraphicsView.DragMode.ScrollHandDrag)
        self.setBackgroundBrush(QBrush(QColor(15, 18, 22))) 
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setTransformationAnchor(QGraphicsView.ViewportAnchor.AnchorUnderMouse)
        
        # Disable pixel-scrolling optimization so the fixed foreground overlay doesn't smear when panning
        self.setViewportUpdateMode(QGraphicsView.ViewportUpdateMode.FullViewportUpdate)

    def mouseDoubleClickEvent(self, event):
        item = self.itemAt(event.pos())
        if item is not None:
            event_idx = item.data(0)
            if event_idx is not None:
                ev = self.main_window.events[event_idx]
                target_x = self.main_window.val_to_x(ev["float_val"])
                
                # Zoom in nicely on the target event horizontally only
                self.resetTransform()
                self.scale(2.5, 1.0) 
                self.centerOn(target_x, item.scenePos().y())
                return
        super().mouseDoubleClickEvent(event)

    def contextMenuEvent(self, event):
        item = self.itemAt(event.pos())
        if item is not None:
            event_idx = item.data(0)
            if event_idx is not None:
                menu = QMenu(self)
                edit_action = menu.addAction("Edit Event")
                remove_action = menu.addAction("Remove Event")
                
                action = menu.exec(event.globalPos())
                if action == edit_action:
                    self.main_window.edit_event(event_idx)
                elif action == remove_action:
                    self.main_window.remove_event(event_idx)
                return
        super().contextMenuEvent(event)

    def wheelEvent(self, event):
        zoom_in_factor = 1.15
        zoom_out_factor = 1 / zoom_in_factor
        
        if event.angleDelta().y() > 0:
            zoom_factor = zoom_in_factor
        else:
            zoom_factor = zoom_out_factor
            
        # Only scale the X axis (time). Scaling Y causes track heights to crush and overlap!
        self.scale(zoom_factor, 1.0)
        self.main_window.recalculate_text_layout()

    def drawBackground(self, painter, rect):
        super().drawBackground(painter, rect)
        
        min_val = rect.left() / PIXELS_PER_YEAR
        max_val = rect.right() / PIXELS_PER_YEAR
        val_range = max_val - min_val
        
        # Descending step sizes down to 1 day (1/300th of a year)
        steps = [
            100000, 10000, 5000, 1000, 500, 100, 50, 10, 5, 1, 
            0.5,       # 5 Months
            0.1,       # 1 Month
            10/300.0,  # 10 Days
            5/300.0,   # 5 Days
            1/300.0    # 1 Day
        ]
        
        # Find the largest step that gives us at least 4 vertical lines on screen
        step = steps[-1]
        for s in steps:
            if val_range / s >= 4:
                step = s
                break
                
        # Draw the vertical grid lines in the background
        grid_pen = QPen(QColor(40, 45, 50))
        grid_pen.setWidth(0) # Width 0 guarantees a crisp 1px line regardless of zoom
        painter.setPen(grid_pen)
        
        start_count = math.floor(min_val / step)
        end_count = math.ceil(max_val / step)
        
        for i in range(start_count, end_count + 1):
            x = (i * step) * PIXELS_PER_YEAR
            # Use QLineF to prevent 32-bit integer overflow crashes at extreme zooms
            painter.drawLine(QLineF(x, rect.top(), x, rect.bottom()))

    def drawForeground(self, painter, rect):
        super().drawForeground(painter, rect)
        
        min_val = rect.left() / PIXELS_PER_YEAR
        max_val = rect.right() / PIXELS_PER_YEAR
        val_range = max_val - min_val
        
        steps = [
            100000, 10000, 5000, 1000, 500, 100, 50, 10, 5, 1, 
            0.5,       # 5 Months
            0.1,       # 1 Month
            10/300.0,  # 10 Days
            5/300.0,   # 5 Days
            1/300.0    # 1 Day
        ]
        
        step = steps[-1]
        for s in steps:
            if val_range / s >= 4:
                step = s
                break
                
        # Reset transform so we can draw fixed, unscaled UI elements overlaying the view
        painter.resetTransform() 
        
        # Draw a solid dark bar at the top of the window to prevent text ghosting
        painter.fillRect(0, 0, self.viewport().width(), 25, QColor(15, 18, 22, 255))
        
        painter.setPen(QColor(180, 180, 180))
        font = QFont("Arial", 10, QFont.Weight.Bold)
        painter.setFont(font)
        
        start_count = math.floor(min_val / step)
        end_count = math.ceil(max_val / step)
        
        for i in range(start_count, end_count + 1):
            v = i * step
            x = v * PIXELS_PER_YEAR
            
            # Translate scene X coordinate to viewport window X coordinate
            view_pt = self.mapFromScene(x, 0)
            
            # Eliminate microscopic floating point drift before extracting dates
            clean_v = round(v * 300) / 300.0 
            year = int(math.floor(clean_v))
            rem = clean_v - year
            total_days = round(rem * 300)
            month = (total_days // 30) + 1
            day = (total_days % 30) + 1
            
            # Format text depending on precision depth
            if step >= 1:
                label = f"Year {year}"
            elif step >= 0.1: # 0.1 float is 1 month
                if day == 1:
                    label = f"{year}-{month:02d}"
                else:
                    label = f"{year}-{month:02d}-{day:02d}"
            else:             # days
                label = f"{year}-{month:02d}-{day:02d}"
                
            painter.drawText(int(view_pt.x()) + 5, 18, label)


class TimelineApp(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("The Hybris Engine: Timeline Architect v2.1")
        self.resize(1200, 700)

        self.timelines = {
            "Main Line": {"y": 0, "color": "#ffeb3b", "start_val": 0.0, "parent": None}
        }
        self.events = [] 
        self.branch_colors = ["#f44336", "#2196f3", "#4caf50", "#9c27b0", "#ff9800", "#00bcd4"]
        self.color_index = 0
        self.current_filepath = None

        self.scene = QGraphicsScene()
        self.view = RiverView(self.scene, self)

        # Menu Bar
        menubar = self.menuBar()
        file_menu = menubar.addMenu("File")
        
        save_action = QAction("Save Project", self)
        save_action.triggered.connect(self.save_project)
        file_menu.addAction(save_action)
        
        load_action = QAction("Load Project", self)
        load_action.triggered.connect(self.load_project)
        file_menu.addAction(load_action)

        # Status Bar for Notifications
        self.setStatusBar(QStatusBar(self))

        # Toolbar
        main_widget = QWidget()
        layout = QVBoxLayout(main_widget)
        toolbar = QHBoxLayout()
        
        btn_event = QPushButton("+ Fixed Event")
        btn_event.clicked.connect(self.prompt_add_event)
        
        btn_branch = QPushButton("+ Branch")
        btn_branch.clicked.connect(self.prompt_add_branch)
        
        btn_goto = QPushButton("Go To Date...")
        btn_goto.clicked.connect(self.prompt_goto_date)

        btn_fit = QPushButton("Fit All to Screen")
        btn_fit.clicked.connect(self.fit_to_screen)

        toolbar.addWidget(btn_event)
        toolbar.addWidget(btn_branch)
        toolbar.addStretch()
        toolbar.addWidget(btn_goto)
        toolbar.addWidget(btn_fit)
        
        layout.addLayout(toolbar)
        layout.addWidget(self.view)
        self.setCentralWidget(main_widget)

        self.render_canvas()
        
        # Load the latest JSON project on startup
        if not self.load_latest_project():
            # Fallback if no files are found
            self.focus_on_val(1788.0)
            self.statusBar().showMessage("Hybris Engine initialized. No recent timeline found.", 5000)
            
        # Autosave timer (every 60 seconds)
        self.autosave_timer = QTimer(self)
        self.autosave_timer.timeout.connect(self.autosave_project)
        self.autosave_timer.start(60000)

    def val_to_x(self, float_val):
        return float_val * PIXELS_PER_YEAR

    def focus_on_val(self, float_val):
        # Reset zoom scale to default 1:1 before centering, to avoid weird offsets
        self.view.resetTransform()
        x_pos = self.val_to_x(float_val)
        self.view.centerOn(x_pos, 0)
        self.recalculate_text_layout()

    def fit_to_screen(self):
        rect = self.scene.sceneRect()
        view_width = self.view.viewport().width()
        
        # Reset transform and explicitly calculate horizontal scale only
        self.view.resetTransform()
        if rect.width() > 0:
            # Leave a tiny 2% visual margin on the sides
            scale_x = (view_width * 0.98) / rect.width()
            self.view.scale(scale_x, 1.0)
            
        self.view.centerOn(rect.center().x(), 0)
        self.recalculate_text_layout()
        self.statusBar().showMessage("Zoomed to fit all timelines.", 3000)

    def prompt_goto_date(self):
        dialog = QDialog(self)
        dialog.setWindowTitle("Jump to Date")
        layout = QVBoxLayout(dialog)
        date_input = HybrisDateWidget(0, 1, 1)
        layout.addWidget(date_input)
        
        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(dialog.accept)
        buttons.rejected.connect(dialog.reject)
        layout.addWidget(buttons)

        if dialog.exec():
            target_val = date_input.get_float_year()
            self.focus_on_val(target_val)
            self.statusBar().showMessage(f"Teleported to {date_input.get_date_str()}.", 3000)

    def prompt_add_event(self):
        dialog = AddEventDialog(self.timelines, self)
        if dialog.exec():
            line_name = dialog.line_combo.currentText()
            float_val = dialog.date_input.get_float_year()
            date_str = dialog.date_input.get_date_str()
            event_name = dialog.name_input.text()
            
            self.events.append({
                "float_val": float_val, "date_str": date_str, "name": event_name, 
                "line_name": line_name, "type": "fixed"
            })
            self.render_canvas()
            self.autosave_project()
            self.statusBar().showMessage(f"Event '{event_name}' added to {line_name}.", 4000)

    def prompt_add_branch(self):
        dialog = AddBranchDialog(self.timelines, self)
        if dialog.exec():
            parent_line = dialog.parent_line_combo.currentText()
            departure_val = dialog.departure_input.get_float_year()
            departure_str = dialog.departure_input.get_date_str()
            arrival_val = dialog.arrival_input.get_float_year()
            
            trigger_name = dialog.trigger_name_input.text()
            branch_name = dialog.branch_name_input.text()

            if branch_name in self.timelines or branch_name.strip() == "":
                self.statusBar().showMessage("Error: Branch name empty or already exists!", 4000)
                return
            
            branch_count = len(self.timelines)
            y_offset = (branch_count * 120) if branch_count % 2 != 0 else -(branch_count * 120)
            color = self.branch_colors[self.color_index % len(self.branch_colors)]
            self.color_index += 1

            self.timelines[branch_name] = {
                "y": y_offset, "color": color, "start_val": arrival_val, "parent": parent_line
            }
            
            self.events.append({
                "float_val": departure_val, "date_str": departure_str, "name": trigger_name, 
                "line_name": parent_line, "type": "trigger", "target_branch": branch_name
            })
            
            self.render_canvas()
            self.autosave_project()
            self.statusBar().showMessage(f"Branch '{branch_name}' created.", 4000)

    def render_canvas(self):
        self.scene.clear()
        
        # Pull real dynamic bounds based on timeline starts & events bounds
        vals = [e["float_val"] for e in self.events]
        for t in self.timelines.values():
            if t["parent"] is not None:
                vals.append(t.get("start_val", 0.0))
        
        if vals:
            # We add 500 years of padding so "Fit To Screen" doesn't clip markers!
            min_val = min(vals) - 500.0
            max_val = max(vals) + 500.0
        else:
            min_val = -500.0
            max_val = 500.0
            
        rect_x = self.val_to_x(min_val)
        rect_w = self.val_to_x(max_val) - rect_x
        self.scene.setSceneRect(rect_x, -2000, rect_w, 4000)

        # 1. DRAW TIMELINES
        drawn_jumps_top = []
        drawn_jumps_bottom = []
        
        for name, data in self.timelines.items():
            y_pos = data["y"]
            start_x = self.val_to_x(data.get("start_val", 0.0))
            color = QColor(data["color"])

            if data["parent"] is None:
                # Infinitely bounds logic: from -5,000,000 years to +5,000,000 years
                inf_min_x = self.val_to_x(-5000000)
                inf_max_x = self.val_to_x(5000000)
                
                main_pen = QPen(color, 4)
                main_pen.setCosmetic(True) # Keeps line thick when zoomed out
                self.scene.addLine(inf_min_x, y_pos, inf_max_x, y_pos, main_pen)
                
                # Add title at the edge of the bounded view width
                self.add_text(rect_x + 100, y_pos - 35, name, color, 14)
            else:
                parent_y = self.timelines[data["parent"]]["y"]
                
                # Retrieve the trigger event location for this jump
                trigger_x = start_x # fallback if none found
                for e in self.events:
                    if e.get("target_branch") == name:
                        trigger_x = self.val_to_x(e["float_val"])
                        break
                
                # --- JUMP ANTI-COLLISION LOGIC ---
                min_x = min(trigger_x, start_x)
                max_x = max(trigger_x, start_x)
                
                is_top = y_pos < parent_y
                base_control_y_offset = 120
                relevant_jumps = drawn_jumps_top if is_top else drawn_jumps_bottom
                
                for (ox1, ox2, o_offset) in relevant_jumps:
                    # check if the horizontal spans of the jumps overlap
                    if not (max_x < ox1 or min_x > ox2):
                        # Push the arc outwards by an extra 70 pixels to stack them perfectly
                        base_control_y_offset = max(base_control_y_offset, o_offset + 70)
                        
                relevant_jumps.append((min_x, max_x, base_control_y_offset))
                
                # Create the arc's vertical peak height pushing OUTWARDS from the cluster of lines
                if is_top:
                    control_y = min(parent_y, y_pos) - base_control_y_offset
                else:
                    control_y = max(parent_y, y_pos) + base_control_y_offset

                # Setup custom "small separated lines" pen with rounded caps
                jump_pen = QPen(color, 3)
                jump_pen.setCosmetic(True) # Keeps jump line visible when zoomed out
                jump_pen.setCapStyle(Qt.PenCapStyle.RoundCap) 
                jump_pen.setDashPattern([1.5, 3]) 
                
                # Setup pen for directional jump arrows
                arrow_pen = QPen(color, 3)
                arrow_pen.setCosmetic(True)
                arrow_pen.setCapStyle(Qt.PenCapStyle.RoundCap)
                arrow_pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)

                def add_jump_arrow(x, y, pointing_down):
                    path = QPainterPath()
                    size = 7
                    if pointing_down:
                        path.moveTo(-size, -size)
                        path.lineTo(0, 0)
                        path.lineTo(size, -size)
                    else:
                        path.moveTo(-size, size)
                        path.lineTo(0, 0)
                        path.lineTo(size, size)
                    
                    arrow_item = self.scene.addPath(path, arrow_pen)
                    arrow_item.setPos(x, y)
                    arrow_item.setFlag(QGraphicsItem.GraphicsItemFlag.ItemIgnoresTransformations)
                    arrow_item.setZValue(50)

                # --- DRAW THE ARC ---
                dx = abs(start_x - trigger_x)
                
                if dx < 1.0:
                    arc_path = QPainterPath()
                    arc_path.moveTo(trigger_x, parent_y)
                    c1_x = trigger_x - 40
                    c2_x = start_x + 40
                    arc_path.cubicTo(c1_x, control_y, c2_x, control_y, start_x, y_pos)
                    self.scene.addPath(arc_path, jump_pen)
                else:
                    cruise = min(75.0, dx / 2.0)
                    dir_x = 1 if start_x >= trigger_x else -1
                    
                    up_end_x = trigger_x + (cruise * dir_x)
                    down_start_x = start_x - (cruise * dir_x)
                    
                    kappa = 0.55228
                    
                    arc_path1 = QPainterPath()
                    arc_path1.moveTo(trigger_x, parent_y)
                    
                    cp1_y = parent_y + (control_y - parent_y) * kappa
                    cp2_x = up_end_x - (cruise * dir_x) * kappa
                    
                    arc_path1.cubicTo(trigger_x, cp1_y, cp2_x, control_y, up_end_x, control_y)
                    self.scene.addPath(arc_path1, jump_pen)
                    
                    # Draw the massive horizontal segment dynamically via a custom item 
                    # so that dashes are only calculated for the visible screen viewport,
                    # avoiding Qt's absolute dash-count crash limits on zoom-in.
                    dash_item = InfiniteDashLineItem(up_end_x, down_start_x, control_y, jump_pen)
                    self.scene.addItem(dash_item)
                    
                    arc_path2 = QPainterPath()
                    arc_path2.moveTo(down_start_x, control_y)
                    
                    cp3_x = down_start_x + (cruise * dir_x) * kappa
                    cp4_y = y_pos + (control_y - y_pos) * kappa
                    
                    arc_path2.cubicTo(cp3_x, control_y, start_x, cp4_y, start_x, y_pos)
                    self.scene.addPath(arc_path2, jump_pen)
                
                # Add Directional Arrows to the vertical jump segments
                dep_y_dir = 1 if control_y > parent_y else -1
                dep_y_pos = parent_y + (40 * dep_y_dir)
                add_jump_arrow(trigger_x, dep_y_pos, pointing_down=(dep_y_dir == 1))
                
                arr_y_dir = 1 if y_pos > control_y else -1
                arr_y_pos = y_pos - (40 * arr_y_dir)
                add_jump_arrow(start_x, arr_y_pos, pointing_down=(arr_y_dir == 1))

                # Draw the Branch Solid Line (From Jump Arrival into infinity)
                branch_pen = QPen(color, 4)
                branch_pen.setCosmetic(True) # Keeps branch line visible when zoomed out
                self.scene.addLine(start_x, y_pos, self.val_to_x(5000000), y_pos, branch_pen)
                
                # Add a marker at the very beginning of the new branch
                # Anchor it to 0,0 and translate it so it doesn't shrink when zooming out
                start_marker_pen = QPen(color)
                start_marker_pen.setCosmetic(True)
                start_marker = self.scene.addEllipse(-6, -6, 12, 12, start_marker_pen, QBrush(color))
                start_marker.setPos(start_x, y_pos)
                start_marker.setFlag(QGraphicsItem.GraphicsItemFlag.ItemIgnoresTransformations)
                
                self.add_text(start_x + 15, y_pos - 30, name, color, 12)

        # 2. DRAW EVENTS
        self.event_graphics = []
        for idx, e in enumerate(self.events):
            if e["line_name"] not in self.timelines: continue 

            line_y = self.timelines[e["line_name"]]["y"]
            x_pos = self.val_to_x(e["float_val"])
            
            if e["type"] == "fixed":
                fixed_pen = QPen(QColor(255, 255, 255), 3)
                fixed_pen.setCosmetic(True) # Stop marker from thinning out
                
                # Anchor to 0,0 and use setPos so the 30px height never scales down
                marker = self.scene.addLine(0, -15, 0, 15, fixed_pen)
                marker.setPos(x_pos, line_y)
                marker.setFlag(QGraphicsItem.GraphicsItemFlag.ItemIgnoresTransformations) 
                marker.setData(0, idx)
                
                txt = self.add_text(0, 0, f"{e['name']}\n[{e['date_str']}]", QColor(200, 200, 200), 10)
                txt.setData(0, idx)
                marker_offset = 15 
                
            elif e["type"] == "trigger":
                trigger_pen = QPen(QColor(self.timelines[e["line_name"]]["color"]), 3)
                trigger_pen.setCosmetic(True) # Stop marker from thinning out
                brush = QBrush(QColor(15, 18, 22)) 
                
                # Anchor to 0,0 and use setPos so the 16px circle never scales down
                marker = self.scene.addEllipse(-8, -8, 16, 16, trigger_pen, brush)
                marker.setPos(x_pos, line_y)
                marker.setFlag(QGraphicsItem.GraphicsItemFlag.ItemIgnoresTransformations) 
                marker.setData(0, idx)
                
                txt = self.add_text(0, 0, f"{e['name']}\n[{e['date_str']}]", QColor(255, 100, 100), 10)
                txt.setData(0, idx)
                marker_offset = 8 
                
            txt.setZValue(100) # Guarantee text is drawn ON TOP of all thick timeline branches

            # Create connection line (hidden by default)
            conn_pen = QPen(QColor(100, 100, 100, 150), 1, Qt.PenStyle.DashLine)
            conn_pen.setCosmetic(True)
            conn_line = self.scene.addLine(0, 0, 0, 0, conn_pen)
            conn_line.setZValue(-1) 
            conn_line.hide()

            self.event_graphics.append({
                "txt": txt,
                "conn_line": conn_line,
                "line_y": line_y,
                "x_pos": x_pos,
                "marker_offset": marker_offset
            })

        # Calculate exact pixel layout based on the current camera scale
        self.recalculate_text_layout()

    def recalculate_text_layout(self):
        if not hasattr(self, 'event_graphics'): return
        
        # Calculate independent axes mapping for scene units to screen pixels
        scale_x = self.view.transform().m11()
        scale_y = self.view.transform().m22()
        if scale_x == 0: scale_x = 1.0
        if scale_y == 0: scale_y = 1.0
        inv_scale_x = 1.0 / scale_x
        inv_scale_y = 1.0 / scale_y
        
        placed_rects = []
        pad_x = 2 * inv_scale_x
        pad_y = 2 * inv_scale_y
        
        for item in self.event_graphics:
            txt = item["txt"]
            conn_line = item["conn_line"]
            line_y = item["line_y"]
            x_pos = item["x_pos"]
            
            local_rect = txt.boundingRect()
            w = local_rect.width() * inv_scale_x
            h = local_rect.height() * inv_scale_y
            
            marker_offset = item["marker_offset"]
            base_x = x_pos - (w / 2) 
            base_y = line_y + marker_offset + (10 * inv_scale_y)
            current_y = base_y
            
            overlap = True
            iterations = 0
            while overlap and iterations < 50:
                overlap = False
                
                rect1_left = base_x - pad_x
                rect1_right = base_x + w + pad_x
                rect1_top = current_y - pad_y
                rect1_bottom = current_y + h + pad_y
                
                for p in placed_rects:
                    if not (rect1_right <= p[0] or rect1_left >= p[1] or rect1_bottom <= p[2] or rect1_top >= p[3]):
                        overlap = True
                        current_y += 35 * inv_scale_y
                        break
                iterations += 1
                
            txt.setPos(base_x, current_y)
            
            if current_y > base_y + (5 * inv_scale_y):
                conn_line.setLine(QLineF(x_pos, line_y + marker_offset, x_pos, current_y))
                conn_line.show()
            else:
                conn_line.hide()
                
            placed_rects.append((base_x, base_x + w, current_y, current_y + h))

    def add_text(self, x, y, text_str, color, size):
        text = self.scene.addText(text_str)
        text.setDefaultTextColor(color)
        text.setFont(QFont("Arial", size, QFont.Weight.Bold))
        text.setPos(x, y)
        # Prevents text elements from resizing when the user zooms in and out
        text.setFlag(QGraphicsItem.GraphicsItemFlag.ItemIgnoresTransformations)
        return text

    def edit_event(self, idx):
        ev = self.events[idx]
        dialog = EditEventDialog(ev, self.timelines, self)
        if dialog.exec():
            ev["line_name"] = dialog.line_combo.currentText()
            ev["float_val"] = dialog.date_input.get_float_year()
            ev["date_str"] = dialog.date_input.get_date_str()
            ev["name"] = dialog.name_input.text()
            
            # If it's a trigger, update the actual arrival branch start date too
            if dialog.is_trigger and dialog.target_branch and dialog.target_branch in self.timelines:
                self.timelines[dialog.target_branch]["start_val"] = dialog.arrival_input.get_float_year()
            
            self.render_canvas()
            self.autosave_project()
            self.statusBar().showMessage(f"Event '{ev['name']}' updated.", 4000)

    def remove_event(self, idx):
        ev = self.events.pop(idx)
        self.render_canvas()
        self.autosave_project()
        self.statusBar().showMessage(f"Event '{ev['name']}' removed.", 4000)

    def save_project(self):
        data = {"timelines": self.timelines, "events": self.events, "color_index": self.color_index}
        filepath, _ = QFileDialog.getSaveFileName(self, "Save Project", "", "JSON Files (*.json)")
        if filepath:
            if not filepath.endswith('.json'): filepath += '.json'
            self.current_filepath = filepath
            with open(filepath, 'w', encoding='utf-8') as f: 
                json.dump(data, f, indent=4)
            self.statusBar().showMessage("Project Saved Successfully.", 4000)

    def autosave_project(self):
        """Silently saves the project to the current file, or autosave.json if none exists."""
        filepath = self.current_filepath if self.current_filepath else "autosave.json"
        data = {"timelines": self.timelines, "events": self.events, "color_index": self.color_index}
        try:
            with open(filepath, 'w', encoding='utf-8') as f: 
                json.dump(data, f, indent=4)
            # We don't display a success message to avoid spamming the status bar
        except Exception as e:
            self.statusBar().showMessage(f"Autosave failed: {e}", 4000)

    def closeEvent(self, event):
        """Ensures the program triggers a final autosave right as you close the application."""
        self.autosave_project()
        event.accept()

    def load_latest_project(self):
        """Scans the current directory for the most recently modified .json file and loads it."""
        json_files = glob.glob("*.json")
        if not json_files:
            return False  # No JSON files found
        
        latest_file = max(json_files, key=os.path.getmtime)
        return self.load_from_file(latest_file)

    def load_project(self):
        """Triggered by UI to let the user select a file to load."""
        filepath, _ = QFileDialog.getOpenFileName(self, "Load Project", "", "JSON Files (*.json)")
        if filepath:
            self.load_from_file(filepath)

    def load_from_file(self, filepath):
        """Core logic to load a specific file, redraw the canvas, and focus the view."""
        try:
            with open(filepath, 'r', encoding='utf-8-sig') as f:
                data = json.load(f)
                self.timelines = data.get("timelines", self.timelines)
                self.events = data.get("events", [])
                self.color_index = data.get("color_index", 0)
                
            # MIGRATION: Patch old JSON files to link branches with their trigger events
            for branch_name, data in self.timelines.items():
                if data["parent"]:
                    for e in self.events:
                        if e["type"] == "trigger" and e["line_name"] == data["parent"] and e.get("target_branch") is None:
                            if abs(e["float_val"] - data["start_val"]) < 0.001:
                                e["target_branch"] = branch_name
                                break
                                
            self.render_canvas()
            
            # Immediately snap camera upon load over existing events so you can see them!
            if self.events:
                self.focus_on_val(self.events[0]["float_val"])
            else:
                self.focus_on_val(0.0)

            self.current_filepath = filepath
            filename = os.path.basename(filepath)
            self.statusBar().showMessage(f"Project '{filename}' Loaded Automatically.", 4000)
            return True
        except Exception as e:
            self.statusBar().showMessage(f"Error loading project: {e}", 4000)
            return False


if __name__ == "__main__":
    app = QApplication(sys.argv)
    app.setStyle("Fusion")
    window = TimelineApp()
    window.show()
    sys.exit(app.exec())